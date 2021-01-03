using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
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
    /// Supports any MPEG-DASH timeserver that emits xs:datetime format timestamps with at exactly millisecond precision.
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

        private SynchronizedTimeSource(TimelineAnchor anchor, Uri xsdatetimeUrl, IHttpClientFactory httpClientFactory)
        {
            _anchor = anchor;
            _xsdatetimeUrl = xsdatetimeUrl;
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

        private readonly Uri _xsdatetimeUrl;
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
                        anchor = await GetTimelineAnchorAsync(_xsdatetimeUrl, client, _cts.Token);
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
        /// <param name="xsdatetimeUrl">A URL that returns the current time in xs:datetime format.</param>
        /// <param name="httpClientFactory">Provides instances of HttpClients on request.</param>
        /// <param name="cancel" />
        public static async Task<SynchronizedTimeSource> CreateAsync(Uri xsdatetimeUrl, IHttpClientFactory httpClientFactory, CancellationToken cancel)
        {
            var client = httpClientFactory.CreateClient();

            // Make a request and throw away the result, to ensure everything in the pipe is warmed up.
            await GetAdjustmentAsync(xsdatetimeUrl, client, cancel);

            // Now do a proper request to synchronize.
            return new SynchronizedTimeSource(await GetTimelineAnchorAsync(xsdatetimeUrl, client, cancel), xsdatetimeUrl, httpClientFactory);
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

            public TimelineAnchor(DateTimeOffset time)
            {
                Time = time;
                SwTicks = Stopwatch.GetTimestamp();
            }
        }

        private static async Task<TimelineAnchor> GetTimelineAnchorAsync(Uri xsdatetimeUrl, HttpClient client, CancellationToken cancel)
        {
            const int batchCount = 3;
            const int requestsPerBrach = 3;

            // We make N batches of M parallel requests, and take the avereage adjustment from all of these as our adjustment to apply.
            // Not necessarily the best strategy but perhaps it helps get rid of the greatest sources of error.
            var adjustments = new List<TimeSpan>(batchCount * requestsPerBrach);

            for (var batch = 0; batch < batchCount; batch++)
            {
                var attempts = new[]
                {
                    GetAdjustmentAsync(xsdatetimeUrl, client, cancel),
                    GetAdjustmentAsync(xsdatetimeUrl, client, cancel),
                    GetAdjustmentAsync(xsdatetimeUrl, client, cancel),
                };

                foreach (var attempt in attempts)
                    adjustments.Add(await attempt);
            }

            var averageAdjustment = TimeSpan.FromSeconds(adjustments.Select(x => x.TotalSeconds).Average());
            var trueTime = DateTimeOffset.UtcNow + averageAdjustment;

            return new TimelineAnchor(trueTime);
        }

        private static async Task<TimeSpan> GetAdjustmentAsync(Uri xsdatetimeUrl, HttpClient client, CancellationToken cancel)
        {
            var rtt = Stopwatch.StartNew();

            var response = await client.GetAsync(xsdatetimeUrl, cancel);
            response.EnsureSuccessStatusCode();

            var length = response.Content.Headers.ContentLength;

            if (length == null)
                throw new NotSupportedException($"Received successful response that was suspiciously lacking a length.");

            if (length > 100)
                throw new NotSupportedException($"Received successful response that was suspiciously long ({length} bytes).");

            var content = await response.Content.ReadAsStringAsync();
            rtt.Stop();

            if (!TryParseXsdatetime(content, out var trueTimeRemote))
                throw new NotSupportedException($"Received successful response that did not contain a valid xs:datetime in any of our supported formats.");

            var localTime = DateTimeOffset.UtcNow;

            // Since it took half the RTT for the response to arrive, we are approximately half the RTT ahead of what the tick count says.
            var rttAdjustmentSeconds = rtt.Elapsed.TotalSeconds / 2;
            var rttAdjustment = TimeSpan.FromSeconds(rttAdjustmentSeconds);
            var trueTime = trueTimeRemote + rttAdjustment;

            // This is the adjustment needed to go from local time to true time.
            return trueTime - localTime;
        }

        // String must conform to the xs:dateTime schema from XML.
        // Time server must be referenced in DASH MPD as urn:mpeg:dash:utc:http-xsdate:2014.
        public const string XsDatetimeCompatibleFormatString = "yyyy-MM-ddTHH:mm:ss.fffZ";

        private static bool TryParseXsdatetime(string timestamp, out DateTimeOffset result) => DateTimeOffset.TryParseExact(timestamp, XsDatetimeCompatibleFormatString, CultureInfo.InvariantCulture, DateTimeStyles.None, out result);
    }
}
