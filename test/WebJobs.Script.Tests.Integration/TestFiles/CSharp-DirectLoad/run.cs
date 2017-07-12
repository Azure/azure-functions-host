// This is content for a test file!  
// Not actually part of the test build. 

using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Host;
using Microsoft.Azure.WebJobs.Extensions.Http;

namespace TestFunction
{
	// Test Functions directly invoking WebJobs. 
    public class DirectLoadFunction
    {
		[FunctionName("DotNetDirectFunction")]
        public static Task<HttpResponseMessage> Run(
			[HttpTrigger(AuthorizationLevel.Anonymous, "get", "post")] HttpRequestMessage req, 
			TraceWriter log)
        {
            log.Info("Test");

            var res = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("Hello from .NET DirectInvoker")
            };

            return Task.FromResult(res);
        }		
    }
}
