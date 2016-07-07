// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Http;
using System.Web.Http.Controllers;
using Microsoft.Azure.WebJobs.Script.Description;
using Microsoft.Azure.WebJobs.Script.WebHost.Filters;
using Microsoft.Azure.WebJobs.Script.WebHost.Kudu;
using Microsoft.Azure.WebJobs.Script.WebHost.Models;
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
        private readonly WebHostSettings _webHostSettings;
        private readonly IFunctionsManager _functionsManager;

        public AdminController(WebScriptHostManager scriptHostManager, WebHostSettings webHostSettings, IFunctionsManager functionsManager)
        {
            _scriptHostManager = scriptHostManager;
            _webHostSettings = webHostSettings;
            _functionsManager = functionsManager;
        }

        [HttpPost]
        [Route("admin/functions/{name}")]
        public async Task<HttpResponseMessage> Invoke(string name, [FromBody] FunctionInvocation invocation)
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

            ParameterDescriptor inputParameter = function.Parameters.First(p => p.IsTrigger);
            Dictionary<string, object> arguments = new Dictionary<string, object>()
            {
                { inputParameter.Name, invocation.Input }
            };
            var runTask = Task.Run(() => _scriptHostManager.Instance.CallAsync(function.Name, arguments));

            if (invocation.WaitForCompletion)
            {
                try
                {
                    await runTask;
                    return Request.CreateResponse(HttpStatusCode.OK);
                }
                catch (Exception e)
                {
                    return Request.CreateErrorResponse(HttpStatusCode.InternalServerError, e);
                }
            }
            else
            {
                return Request.CreateResponse(HttpStatusCode.Accepted);
            }
        }

        [HttpGet]
        [Route("admin/functions/{name}/status")]
        public FunctionStatus GetFunctionStatus(string name)
        {
            Collection<string> functionErrors = null;
            FunctionDescriptor function = _scriptHostManager.Instance.Functions.FirstOrDefault(p => p.Name.ToLowerInvariant() == name.ToLowerInvariant());
            FunctionStatus status = new FunctionStatus
            {
                Metadata = function?.Metadata
            };

            // first see if the function has any errors
            if (_scriptHostManager.Instance.FunctionErrors.TryGetValue(name, out functionErrors))
            {
                status.Errors = functionErrors;
            }
            else
            {
                // if we don't have any errors registered, make sure the function exists
                // before returning empty errors
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
            HostStatus status = new HostStatus
            {
                Id = _scriptHostManager.Instance?.ScriptConfig.HostConfig.HostId,
                WebHostSettings = _webHostSettings,
                ProcessId = Process.GetCurrentProcess().Id,
                IsDebuggerAttached = Debugger.IsAttached
            };

            var lastError = _scriptHostManager.LastError;
            if (lastError != null)
            {
                status.Errors = new Collection<string>();
                status.Errors.Add(Utility.FlattenException(lastError));
            }

            return status;
        }

        [HttpPost]
        [Route("admin/host/debug")]
        public bool LaunchDebugger()
        {
            if (_webHostSettings.IsSelfHost)
            {
                return Debugger.Launch();
            }
            return false;
        }

        [HttpPut]
        [Route("admin/functions/{name}")]
        public async Task<HttpResponseMessage> CreateOrUpdate(string name, [FromBody]FunctionEnvelope functionEnvelope)
        {
            return Request.CreateResponse(HttpStatusCode.Accepted, await _functionsManager.CreateOrUpdateAsync(name, functionEnvelope));
        }

        [HttpGet]
        [Route("admin/functions")]
        public async Task<IEnumerable<FunctionEnvelope>> List()
        {
            return await _functionsManager.ListFunctionsConfigAsync();
            //return Request.CreateResponse(HttpStatusCode.OK, functions);
        }

        [HttpGet]
        [Route("admin/functions/{name}")]
        public async Task<HttpResponseMessage> Get(string name)
        {
            return Request.CreateResponse(HttpStatusCode.OK, await _functionsManager.GetFunctionConfigAsync(name));
        }

        [HttpGet]
        [Route("admin/functions/{name}/secrets")]
        public async Task<HttpResponseMessage> GetSecrets(string name)
        {
            return Request.CreateResponse(HttpStatusCode.OK, await _functionsManager.GetFunctionSecretsAsync(name));
        }

        [HttpDelete]
        [Route("admin/functions/{name}")]
        public HttpResponseMessage Delete(string name)
        {
            _functionsManager.DeleteFunction(name);
            return Request.CreateResponse(HttpStatusCode.NoContent);
        }

        [HttpGet]
        [Route("admin/functions/config")]
        public async Task<HttpResponseMessage> GetHostSettings()
        {
            return Request.CreateResponse(HttpStatusCode.OK, await _functionsManager.GetHostConfigAsync());
        }

        [HttpPut]
        [Route("admin/functions/config")]
        public async Task<HttpResponseMessage> PutHostSettings()
        {
            return Request.CreateResponse(HttpStatusCode.Created, await _functionsManager.PutHostConfigAsync(await Request.Content.ReadAsAsync<JObject>()));
        }

        [HttpPost]
        [Route("admin/run/vscode")]
        public HttpResponseMessage LaunchVsCode()
        {
            Process.Start(@"C:\Program Files (x86)\Microsoft VS Code\Code.exe", System.Environment.CurrentDirectory);
            return Request.CreateResponse(HttpStatusCode.Accepted);
        }

        public override Task<HttpResponseMessage> ExecuteAsync(HttpControllerContext controllerContext, CancellationToken cancellationToken)
        {
            // For all admin api requests, we'll update the ScriptHost debug timeout
            // For now, we'll enable debug mode on ANY admin requests. Since the Portal interacts through
            // the admin API this is sufficient for identifying when the Portal is connected.
            _scriptHostManager.Instance?.NotifyDebug();
            return base.ExecuteAsync(controllerContext, cancellationToken);
        }
    }
}
