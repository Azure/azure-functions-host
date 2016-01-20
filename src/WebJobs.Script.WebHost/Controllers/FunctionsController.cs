using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Http;
using System.Web.Http.Controllers;

namespace WebJobs.Script.WebHost.Controllers
{
    /// <summary>
    /// Controller responsible for handling all http function invocations
    /// on route /functions
    /// </summary>
    public class FunctionsController : ApiController
    {
        private readonly WebScriptHostManager _scriptHostManager;

        public FunctionsController(WebScriptHostManager scriptHostManager)
        {
            _scriptHostManager = scriptHostManager;
        }

        public override async Task<HttpResponseMessage> ExecuteAsync(HttpControllerContext controllerContext, CancellationToken cancellationToken)
        {
            // TODO: Need to add authentication filters to the request pipeline

            HttpResponseMessage response = await _scriptHostManager.HandleRequestAsync(controllerContext.Request, cancellationToken);

            return response;
        }
    }
}
