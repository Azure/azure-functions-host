using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using System.Web.Http;
using Microsoft.Azure.WebJobs.Script;
using Newtonsoft.Json.Linq;

namespace WebJobs.Script.WebHost.Controllers
{
    /// <summary>
    /// Controller responsible for handling all administrative requests, for
    /// example enqueueing function invocations, etc.
    /// </summary>
    public class AdminController : ApiController
    {
        private readonly WebScriptHostManager _scriptHostManager;
        private readonly FunctionInvocationManager _invocationManager;

        public AdminController(WebScriptHostManager scriptHostManager, FunctionInvocationManager invocationManager)
        {
            _scriptHostManager = scriptHostManager;
            _invocationManager = invocationManager;
        }

        [HttpPost]
        [Route("admin/functions/{name}")]
        public async Task<HttpResponseMessage> RunAsync(string name)
        {
            // TODO: This entire controller will need to be locked down once the
            // admin auth model is in place

            FunctionDescriptor function = _scriptHostManager.Instance.Functions.FirstOrDefault(p => p.Name.ToLowerInvariant() == name.ToLowerInvariant());
            if (function == null)
            {
                return new HttpResponseMessage(HttpStatusCode.NotFound);
            }

            // Enqueue an invoke message for asynchronous processing
            // While we could also do a direct invocation here on the host via
            // JobHost.CallAsync, enqueueing a message allows us to control the
            // invocation ID (so we can return it for deep linking to Dashboard),
            // and we also get the benefits of scale out if multiple hosts are running.
            Guid id = Guid.NewGuid();
            string input = await this.ControllerContext.Request.Content.ReadAsStringAsync();
            ParameterDescriptor inputParameter = function.Parameters.First();
            _invocationManager.Enqueue(id, function.Name, input, inputParameter);

            // return a successfull status code indicating the request is in progress
            HttpResponseMessage response = new HttpResponseMessage(HttpStatusCode.Accepted);
            JObject result = new JObject()
            {
                { "id", id }
            };
            response.Content = new StringContent(result.ToString());

            return response;
        }
    }
}
