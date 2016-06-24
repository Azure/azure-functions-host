// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using System.Web.Http;
using Microsoft.Azure.WebJobs.Script.Description;
using Microsoft.Azure.WebJobs.Script.WebHost.Filters;
using Microsoft.Azure.WebJobs.Script.WebHost.Models;
using Microsoft.Azure.WebJobs.Script.WebHost.Kudu;
using Newtonsoft.Json.Linq;

namespace Microsoft.Azure.WebJobs.Script.WebHost.Controllers
{
    /// <summary>
    /// Controller responsible for handling all administrative requests, for
    /// example enqueueing function invocations, etc.
    /// </summary>
    [AuthorizationLevel(AuthorizationLevel.Admin)]
    public class AdminController : ApiController
    {
        private readonly WebScriptHostManager _scriptHostManager;
        private readonly IFunctionsManager _manager;

        public AdminController(WebScriptHostManager scriptHostManager, IFunctionsManager manager)
        {
            _scriptHostManager = scriptHostManager;
            _manager = manager;
        }

        [HttpPost]
        [Route("admin/functions/{name}")]
        public HttpResponseMessage Invoke(string name, [FromBody] FunctionInvocation invocation)
        {
            if (invocation == null)
            {
                return new HttpResponseMessage(HttpStatusCode.BadRequest);
            }

            FunctionDescriptor function = _scriptHostManager.Instance.Functions.FirstOrDefault(p => p.Name.ToLowerInvariant() == name.ToLowerInvariant());
            if (function == null)
            {
                return new HttpResponseMessage(HttpStatusCode.NotFound);
            }

            ParameterDescriptor inputParameter = function.Parameters.First();
            Dictionary<string, object> arguments = new Dictionary<string, object>()
            {
                { inputParameter.Name, invocation.Input }
            };
            Task.Run(() => _scriptHostManager.Instance.CallAsync(function.Name, arguments));

            return new HttpResponseMessage(HttpStatusCode.Accepted);
        }

        [HttpGet]
        [Route("admin/functions/{name}/status")]
        public FunctionStatus GetFunctionStatus(string name)
        {
            FunctionStatus status = new FunctionStatus();
            Collection<string> functionErrors = null;

            // first see if the function has any errors
            if (_scriptHostManager.Instance.FunctionErrors.TryGetValue(name, out functionErrors))
            {
                status.Errors = functionErrors;
            }
            else
            {
                // if we don't have any errors registered, make sure the function exists
                // before returning empty errors
                FunctionDescriptor function = _scriptHostManager.Instance.Functions.FirstOrDefault(p => p.Name.ToLowerInvariant() == name.ToLowerInvariant());
                if (function == null)
                {
                    throw new HttpResponseException(HttpStatusCode.NotFound);
                }
            }

            return status;
        }

        [HttpGet]
        [Route("admin/host/status")]
        public HostStatus GetHostStatus()
        {
            HostStatus status = new HostStatus();

            var lastError = _scriptHostManager.LastError;
            if (lastError != null)
            {
                status.Errors = new Collection<string>();
                status.Errors.Add(Utility.FlattenException(lastError));
            }

            return status;
        }

        [HttpPut]
        [Route("api/functions/{name}")]
        public async Task<HttpResponseMessage> CreateOrUpdate(string name, [FromBody]FunctionEnvelope functionEnvelope)
        {
            return Request.CreateResponse(HttpStatusCode.Accepted, await _manager.CreateOrUpdateAsync(name, functionEnvelope));
        }

        [HttpGet]
        [Route("api/functions")]
        public Task<IEnumerable<FunctionEnvelope>> List()
        {
            return _manager.ListFunctionsConfigAsync();
        }

        [HttpGet]
        [Route("api/functions/{name}")]
        public Task<FunctionEnvelope> Get(string name)
        {
            return _manager.GetFunctionConfigAsync(name);
        }

        [HttpGet]
        [Route("api/functions/{name}/secrets")]
        public Task<Kudu.FunctionSecrets> GetSecrets(string name)
        {
            return _manager.GetFunctionSecretsAsync(name);
        }

        [HttpDelete]
        [Route("api/functions/{name}")]
        public HttpResponseMessage Delete(string name)
        {
            _manager.DeleteFunction(name);
            return Request.CreateResponse(HttpStatusCode.NoContent);
        }

        [HttpGet]
        [Route("api/functions/config")]
        public Task<JObject> GetHostSettings()
        {
            return _manager.GetHostConfigAsync();
        }

        [HttpPut]
        [Route("api/functions/config")]
        public async Task<HttpResponseMessage> PutHostSettings()
        {
            return Request.CreateResponse(HttpStatusCode.Created, await _manager.PutHostConfigAsync(await Request.Content.ReadAsAsync<JObject>()));
        }

        [HttpPost]
        [Route("admin/run/vscode")]
        public HttpResponseMessage LaunchVsCode()
        {
            Process.Start(@"C:\Program Files (x86)\Microsoft VS Code\Code.exe", System.Environment.CurrentDirectory);
            return Request.CreateResponse(HttpStatusCode.Accepted);
        }
    }
}
