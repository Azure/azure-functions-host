using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Http;

namespace Dashboard.Results
{
    public class TextResult : IHttpActionResult
    {
        private readonly string _content;
        private readonly HttpRequestMessage _request;

        public TextResult(string content, HttpRequestMessage request)
        {
            _content = content;
            _request = request;
        }

        public Task<HttpResponseMessage> ExecuteAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult(Execute());
        }

        private HttpResponseMessage Execute()
        {
            HttpResponseMessage response = new HttpResponseMessage(HttpStatusCode.OK);
            response.Content = new StringContent(_content);
            response.RequestMessage = _request;
            return response;
        }
    }
}
