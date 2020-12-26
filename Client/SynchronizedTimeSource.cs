using System;
using System.Diagnostics;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace DashTimeserver.Client
{
    /// <summary>
    /// Obtains a reference time from a DASH timeserver and supplies it (in RTT-adjusted form) to callers.
    /// Performs periodic refreshes to maintain sync over time.
    /// </summary>
    /// <remarks>
    /// Thread-safe.
    /// 
    /// The first sync is required to succeed. Any background updates may fail and be silently ignored.
    /// </remarks>
    public sealed class SynchronizedTimeSource : IAsyncDisposable
    {
        private static readonly TimeSpan BackgroundUpdateInterval = TimeSpan.FromSeconds(60);

        public DateTimeOffset GetCurrentTime()
        {
            lock (_lock)
            {
                var swTickDelta = Stopwatch.GetTimestamp() - _anchor.SwTicks;
                var secondsDelta = swTickDelta * 1.0 / Stopwatch.Frequency;

                return _anchor.Time.AddSeconds(secondsDelta);
            }
        }

        private SynchronizedTimeSource(TimelineAnchor anchor, Uri url, IHttpClientFactory httpClientFactory)
        {
            _anchor = anchor;
            _url = url;
            _httpClientFactory = httpClientFactory;

            _backgroundUpdateTask = Task.Run(PerformBackgroundUpdatesAsync);
        }

        public async ValueTask DisposeAsync()
        {
            _cts.Cancel();

            await _backgroundUpdateTask;

            _cts.Dispose();
        }

        private TimelineAnchor _anchor;

        private readonly Uri _url;
        private readonly IHttpClientFactory _httpClientFactory;

        private readonly object _lock = new object();

        private readonly CancellationTokenSource _cts = new CancellationTokenSource();

        private readonly Task _backgroundUpdateTask;

        private async Task PerformBackgroundUpdatesAsync()
        {
            while (!_cts.IsCancellationRequested)
            {
                try
                {
                    await Task.Delay(BackgroundUpdateInterval, _cts.Token);

                    var client = _httpClientFactory.CreateClient();

                    TimelineAnchor anchor;

                    try
                    {
                        anchor = await GetBestTimelineAnchorAsync(_url, client, _cts.Token);
                    }
                    catch
                    {
                        // It's fine in some updates fail to be processed.
                        // We will keep ticking even without network connectivity.
                        continue;
                    }

                    lock (_lock)
                        _anchor = anchor;
                }
                catch (OperationCanceledException) when (_cts.IsCancellationRequested)
                {
                }
            }
        }

        /// <summary>
        /// Obtains the wall clock time from the indicated timeserver and returns an instance that will keep tracking this timeserver.
        /// 
        /// The method returns once synchronization has been established.
        /// </summary>
        /// <param name="timeserverUrl">The root URL to the timserver, without any path/suffix. Query string parameters will be preserved.</param>
        /// <param name="httpClientFactory">Provides instances of HttpClients on request.</param>
        /// <param name="cancel" />
        public static async Task<SynchronizedTimeSource> CreateAsync(Uri timeserverUrl, IHttpClientFactory httpClientFactory, CancellationToken cancel)
        {
            // We want the tick-based API, as it is simpler to parse.
            // We preserve the query string since it may carry useful debugging parameters like offset.
            var url = new Uri(new Uri(timeserverUrl, "utcticks"), timeserverUrl.Query);

            var client = httpClientFactory.CreateClient();

            // Make a request and throw away the result, to ensure everything is warmed up.
            await GetTimelineAnchorAsync(url, client, cancel);

            // Now do a proper request to synchronize.
            return new SynchronizedTimeSource(await GetBestTimelineAnchorAsync(url, client, cancel), url, httpClientFactory);
        }

        private struct TimelineAnchor
        {
            /// <summary>
            /// A moment in time where we cast our anchor as a result of synchronization.
            /// </summary>
            public DateTimeOffset Time { get; }

            /// <summary>
            /// The Stopwatch timestamp at the indicated local time.
            /// This lets us follow the synchronized timeline independent of local date/time modifications (which do not affect Stopwatch).
            /// </summary>
            public long SwTicks { get; }

            /// <summary>
            /// The RTT of the synchronization attempt that produced this result.
            /// </summary>
            public TimeSpan Rtt { get; }

            public TimelineAnchor(DateTimeOffset time, long swTicks, TimeSpan rtt)
            {
                Time = time;
                SwTicks = swTicks;
                Rtt = rtt;
            }
        }

        private static async Task<TimelineAnchor> GetBestTimelineAnchorAsync(Uri utcticksUrl, HttpClient client, CancellationToken cancel)
        {
            // We make 3 parallel requests, and keep the one with minimum RTT, in a hope to throw away any outliers that were slowed down for no good reason.
            // Not necessarily the best strategy but perhaps it helps get rid of the greatest sources of error.
            var attempts = new[]
            {
                GetTimelineAnchorAsync(utcticksUrl, client, cancel),
                GetTimelineAnchorAsync(utcticksUrl, client, cancel),
                GetTimelineAnchorAsync(utcticksUrl, client, cancel),
            };

            await Task.WhenAll(attempts);

            return attempts.OrderBy(x => x.Result.Rtt).First().Result;
        }

        private static async Task<TimelineAnchor> GetTimelineAnchorAsync(Uri utcticksUrl, HttpClient client, CancellationToken cancel)
        {
            var rtt = Stopwatch.StartNew();

            var response = await client.GetAsync(utcticksUrl, cancel);
            response.EnsureSuccessStatusCode();

            var length = response.Content.Headers.ContentLength;

            if (length == null)
                throw new NotSupportedException($"Received successful response that was suspiciously lacking a length.");

            if (length > 100)
                throw new NotSupportedException($"Received successful response that was suspiciously long ({length} bytes).");

            var content = await response.Content.ReadAsStringAsync();
            rtt.Stop();

            var localSwTicks = Stopwatch.GetTimestamp();

            if (!long.TryParse(content, out var trueTimeTicks))
                throw new NotSupportedException($"Received successful response that did not contain a valid tick count.");

            var trueTime = new DateTimeOffset(trueTimeTicks, TimeSpan.Zero);

            // Since it took half the RTT for the response to arrive, we are approximately half the RTT ahead of what the tick count says.
            var rttAdjustmentSeconds = rtt.Elapsed.TotalSeconds / 2;
            var rttAdjustmentSwTicks = (long)(rttAdjustmentSeconds * Stopwatch.Frequency);

            var adjustedTrueTime = trueTime.AddSeconds(rttAdjustmentSeconds);

            return new TimelineAnchor(adjustedTrueTime, localSwTicks + rttAdjustmentSwTicks, rtt.Elapsed);
        }
    }
}
