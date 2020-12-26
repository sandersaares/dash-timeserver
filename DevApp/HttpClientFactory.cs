using System.Net.Http;

namespace DevApp
{
    public sealed class HttpClientFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new HttpClient();
    }
}
