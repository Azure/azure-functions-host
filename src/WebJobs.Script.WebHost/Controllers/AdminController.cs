﻿// Copyright (c) .NET Foundation. All rights reserved.
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
using Microsoft.Azure.WebJobs.Host;
using Microsoft.Azure.WebJobs.Host.Config;
using Microsoft.Azure.WebJobs.Script.Config;
using Microsoft.Azure.WebJobs.Script.Description;
using Microsoft.Azure.WebJobs.Script.WebHost.Filters;
using Microsoft.Azure.WebJobs.Script.WebHost.Models;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;

namespace Microsoft.Azure.WebJobs.Script.WebHost.Controllers
{
    /// <summary>
    /// Controller responsible for handling all administrative requests, for
    /// example enqueueing function invocations, etc.
    /// </summary>
    [JwtAuthentication]
    [AuthorizationLevel(AuthorizationLevel.Admin)]
    public class AdminController : ApiController
    {
        private readonly WebScriptHostManager _scriptHostManager;
        private readonly WebHostSettings _webHostSettings;
        private readonly TraceWriter _traceWriter;
        private readonly ILogger _logger;

        [CLSCompliant(false)]
        public AdminController(WebScriptHostManager scriptHostManager, WebHostSettings webHostSettings, TraceWriter traceWriter, ILoggerFactory loggerFactory)
        {
            _scriptHostManager = scriptHostManager;
            _webHostSettings = webHostSettings;
            _traceWriter = traceWriter.WithSource($"{ScriptConstants.TraceSourceHostAdmin}.Api");
            _logger = loggerFactory?.CreateLogger("Host.Admin.Api");
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

            ParameterDescriptor inputParameter = function.Parameters.First(p => p.IsTrigger);
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
        [AllowAnonymous]
        public IHttpActionResult GetHostStatus()
        {
            var authorizationLevel = Request.GetAuthorizationLevel();
            if (authorizationLevel == AuthorizationLevel.Admin ||
                Request.IsAntaresInternalRequest())
            {
                var status = new HostStatus
                {
                    State = _scriptHostManager.State.ToString(),
                    Version = ScriptHost.Version,
                    Id = _scriptHostManager.Instance?.ScriptConfig.HostConfig.HostId
                };

                var lastError = _scriptHostManager.LastError;
                if (lastError != null)
                {
                    status.Errors = new Collection<string>();
                    status.Errors.Add(Utility.FlattenException(lastError));
                }

                var parameters = Request.GetQueryParameterDictionary();
                string value = null;
                if (parameters.TryGetValue(ScriptConstants.CheckLoadQueryParameterName, out value) && value == "1")
                {
                    status.Load = new LoadStatus
                    {
                        IsHigh = _scriptHostManager.PerformanceManager.IsUnderHighLoad()
                    };
                }

                string message = $"Host Status: {JsonConvert.SerializeObject(status, Formatting.Indented)}";
                _traceWriter.Info(message);
                _logger?.LogInformation(message);

                return Ok(status);
            }
            else
            {
                return Unauthorized();
            }
        }

        [HttpPost]
        [Route("admin/host/ping")]
        [AllowAnonymous]
        public IHttpActionResult Ping()
        {
            return Ok();
        }

        [HttpPost]
        [Route("admin/host/debug")]
        public HttpResponseMessage LaunchDebugger()
        {
            if (_webHostSettings.IsSelfHost)
            {
                // If debugger is already running, this will be a no-op returning true.
                if (Debugger.Launch())
                {
                    return new HttpResponseMessage(HttpStatusCode.OK);
                }
                else
                {
                    return new HttpResponseMessage(HttpStatusCode.Conflict);
                }
            }
            return new HttpResponseMessage(HttpStatusCode.NotImplemented);
        }

        public override Task<HttpResponseMessage> ExecuteAsync(HttpControllerContext controllerContext, CancellationToken cancellationToken)
        {
            // For all admin api requests, we'll update the ScriptHost debug timeout
            // For now, we'll enable debug mode on ANY admin requests. Since the Portal interacts through
            // the admin API this is sufficient for identifying when the Portal is connected.
            _scriptHostManager.Instance?.NotifyDebug();

            return base.ExecuteAsync(controllerContext, cancellationToken);
        }

        [Route("admin/extensions/{name}/{*extra}")]
        [HttpGet]
        [HttpPost]
        [AllowAnonymous]
        public async Task<HttpResponseMessage> ExtensionHook(string name, CancellationToken token)
        {
            var provider = this._scriptHostManager.BindingWebHookProvider;

            var hook = provider.GetHandlerOrNull(name);
            if (hook != null)
            {
                var response = await hook.ConvertAsync(this.Request, token);
                return response;
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        }

        // Provides the URL for accessing the admin/hook route.
        internal static Uri GetRouteForExtensionHook(string name)
        {
            var settings = ScriptSettingsManager.Instance;

            var hostName = settings.GetSetting(EnvironmentSettingNames.AzureWebsiteHostName);
            if (hostName == null)
            {
                return null;
            }

            bool isLocalhost = hostName.StartsWith("localhost:", StringComparison.OrdinalIgnoreCase);
            var scheme = isLocalhost ? "http" : "https";

            return new Uri($"{scheme}://{hostName}/admin/extensions/{name}");
        }
    }
}
