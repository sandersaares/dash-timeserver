using DashTimeserver.Client;
using System;
using System.Threading.Tasks;

namespace DevApp
{
    class Program
    {
        private const string FormatString = "yyyy-MM-ddTHH:mm:ss.fffffZ";

        static async Task Main(string[] args)
        {
            var baseUrl = new Uri("http://localhost:64868/");
            var factory = new HttpClientFactory();

            var sts = await SynchronizedTimeSource.CreateAsync(baseUrl, factory, default);

            while (true)
            {
                var trueTime = sts.GetCurrentTime();
                var localTime = DateTimeOffset.UtcNow;

                Console.WriteLine($"Local: {localTime.ToString(FormatString)}");
                Console.WriteLine($" True: {trueTime.ToString(FormatString)}");
                Console.WriteLine($"Delta: {(trueTime - localTime).TotalMilliseconds:N0} ms");
                Console.WriteLine();

                await Task.Delay(TimeSpan.FromSeconds(1));
            }
        }
    }
}
