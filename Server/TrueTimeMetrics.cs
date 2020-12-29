using Koek;
using Prometheus;
using System;

namespace DashTimeserver.Server
{
    /// <summary>
    /// Reports the synchronized true time to Prometheus.
    /// </summary>
    public sealed class TrueTimeMetrics : IDisposable
    {
        public TrueTimeMetrics(ITimeSource timeSource)
        {
            _timeSource = timeSource;
        }

        public void Dispose()
        {
            // We deactivate ourselves on disposal (although as there is no "unregister callback" option with the registry so we stay in memory).
            _disposed = true;
        }

        private readonly ITimeSource _timeSource;

        private bool _disposed;

        public void Register(CollectorRegistry? registry = null)
        {
            registry ??= Metrics.DefaultRegistry;
            var factory = Metrics.WithCustomRegistry(registry);

            var instanceDisposed = false;

            var instance = factory.CreateGauge("synchronized_unixtime_seconds", "Synchronized time in Unix timestamp format.");

            registry.AddBeforeCollectCallback(delegate
            {
                if (instanceDisposed)
                    return;

                if (_disposed)
                {
                    instance.Unpublish();
                    instanceDisposed = true;
                    return;
                }

                instance.Set(ToUnixTimeSecondsAsDouble(_timeSource.GetCurrentTime()));
            });
        }

        // Math copypasted from DateTimeOffset.cs in .NET Framework.

        // Number of days in a non-leap year
        private const int DaysPerYear = 365;
        // Number of days in 4 years
        private const int DaysPer4Years = DaysPerYear * 4 + 1;       // 1461
        // Number of days in 100 years
        private const int DaysPer100Years = DaysPer4Years * 25 - 1;  // 36524
        // Number of days in 400 years
        private const int DaysPer400Years = DaysPer100Years * 4 + 1; // 146097
        private const int DaysTo1970 = DaysPer400Years * 4 + DaysPer100Years * 3 + DaysPer4Years * 17 + DaysPerYear; // 719,162
        private const long UnixEpochTicks = TimeSpan.TicksPerDay * DaysTo1970; // 621,355,968,000,000,000
        private const long UnixEpochSeconds = UnixEpochTicks / TimeSpan.TicksPerSecond; // 62,135,596,800

        private static double ToUnixTimeSecondsAsDouble(DateTimeOffset timestamp)
        {
            // This gets us sub-millisecond precision, which is better than ToUnixTimeMilliseconds().
            var ticksSinceUnixEpoch = timestamp.ToUniversalTime().Ticks - UnixEpochSeconds * TimeSpan.TicksPerSecond;
            return ticksSinceUnixEpoch / (double)TimeSpan.TicksPerSecond;
        }
    }
}
