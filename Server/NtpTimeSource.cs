using GuerrillaNtp;
using Koek;
using Microsoft.Extensions.Logging;
using System;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;

namespace DashTimeserver.Server
{
    public sealed class NtpTimeSource : ITimeSource, IAsyncDisposable
    {
        private const string Timeserver = "time.windows.com";

        /// <summary>
        /// Every once in a while we update the time from the NTP server.
        /// </summary>
        private static readonly TimeSpan RefreshInterval = TimeSpan.FromMinutes(1);

        public NtpTimeSource(ILogger<NtpTimeSource> logger)
        {
            _log = logger;

            _backgroundUpdatesTask = Task.Run(PerformBackgroundUpdatesAsync);
        }

        public async ValueTask DisposeAsync()
        {
            _cts.Cancel();

            await _backgroundUpdatesTask;
        }

        private readonly CancellationTokenSource _cts = new();
        private readonly Task _backgroundUpdatesTask;

        private async Task PerformBackgroundUpdatesAsync()
        {
            while (!_cts.IsCancellationRequested)
            {
                try
                {
                    var address = (await Dns.GetHostAddressesAsync(Timeserver).WithAbandonment(_cts.Token)).FirstOrDefault();

                    if (address == null)
                        throw new ContractException("NTP server could not be resolved to an IP address. DNS glitch?");

                    using var client = new NtpClient(address);
                    _offset = client.GetCorrectionOffset();

                    _log.LogInformation($"Time synchronized from NTP. New offset: {_offset.TotalSeconds:F3} seconds. True time: {GetCurrentTime().ToString(Constants.XsDatetimeCompatibleFormatString)}.");
                }
                catch (Exception ex)
                {
                    _log.LogError(ex, $"NTP time synchronization failed: {ex}");
                }

                try
                {
                    await Task.Delay(RefreshInterval, _cts.Token);
                }
                catch (OperationCanceledException) when (_cts.IsCancellationRequested)
                {
                    // OK, we are exiting.
                }
            }

            _log.LogTrace("Stopping background updates due to signal.");
        }

        /// <summary>
        /// The offset to add to local PC time in order to get the time that the NTP server considers correct.
        /// </summary>
        private TimeSpan _offset = TimeSpan.Zero;

        public DateTimeOffset GetCurrentTime() => DateTimeOffset.UtcNow + _offset;

        private readonly ILogger _log;
    }
}
