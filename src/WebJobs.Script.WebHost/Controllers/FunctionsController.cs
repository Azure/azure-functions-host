using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Http;
using System.Web.Http.Controllers;
using WebJobs.Script.WebHost.WebHooks;

namespace WebJobs.Script.WebHost.Controllers
{
    /// <summary>
    /// Controller responsible for handling all http function invocations
    /// on route /functions
    /// </summary>
    public class FunctionsController : ApiController
    {
        private readonly WebScriptHostManager _scriptHostManager;
        private WebHookReceiverManager _webHookReceiverManager;

        public FunctionsController(WebScriptHostManager scriptHostManager, WebHookReceiverManager webHookReceiverManager)
        {
            _scriptHostManager = scriptHostManager;
            _webHookReceiverManager = webHookReceiverManager;
        }

        public override async Task<HttpResponseMessage> ExecuteAsync(HttpControllerContext controllerContext, CancellationToken cancellationToken)
        {
            HttpRequestMessage request = controllerContext.Request;

            // First see if the request maps to a function
            HttpFunctionInfo function = _scriptHostManager.GetHttpFunctionOrNull(request.RequestUri);
            if (function == null)
            {
                return new HttpResponseMessage(HttpStatusCode.NotFound);
            }

            HttpResponseMessage response = null;
            if (function.IsWebHook)
            {
                // This is a WebHook request so define a delegate for the user function.
                // The WebHook Receiver pipeline will first validate the request fully
                // then invoke this callback.
                Func<HttpRequestMessage, Task<HttpResponseMessage>> invokeFunction = async (req) =>
                {
                    return await _scriptHostManager.HandleRequestAsync(function, req, cancellationToken);
                };
                response = await _webHookReceiverManager.HandleRequestAsync(function, request, invokeFunction);
            }
            else
            {
                // Not a WebHook request so dispatch directly
                response = await _scriptHostManager.HandleRequestAsync(function, request, cancellationToken);
            }
            
            return response;
        }
    }
}
