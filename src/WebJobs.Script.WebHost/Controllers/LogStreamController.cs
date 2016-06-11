using Microsoft.Azure.WebJobs.Script.WebHost.Kudu;
using Microsoft.Azure.WebJobs.Script.WebHost.Streams;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;
using System.Web;
using System.Web.Http;
using System.Web.Http.Controllers;

namespace Microsoft.Azure.WebJobs.Script.WebHost.Controllers
{
    public class LogStreamController : ApiController
    {
        private const string FilterQueryKey = "filter";
        private readonly IEnvironment _environment;
        private readonly ITracer _tracer;

        public LogStreamController(IEnvironment environment, ITracer tracer)
        {
            _tracer = tracer;
            _environment = environment;
        }

        public override Task<HttpResponseMessage> ExecuteAsync(HttpControllerContext controllerContext, CancellationToken cancellationToken)
        {
            var request = controllerContext.Request;
            var filter = request.RequestUri.ParseQueryString()[FilterQueryKey];
            var routePath = request.RequestUri.AbsolutePath.Substring("/api/logstream".Length).Trim('/');
            var firstPath = routePath.Split(new char[] { '/' }, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();

            var path = FileSystemHelpers.EnsureDirectory(Path.Combine(_environment.ApplicationLogFilesPath, routePath));

            var logStream = new LogStream(path, filter, _tracer);

            var response = request.CreateResponse();
            response.Content = new PushStreamContent((Action<Stream, HttpContent, TransportContext>)logStream.SetStream, new MediaTypeHeaderValue("test/custom"));

            return Task.FromResult(response);
        }
    }
}