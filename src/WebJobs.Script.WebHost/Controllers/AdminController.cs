// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.WebApiCompatShim;
using Microsoft.Azure.WebJobs.Host;
using Microsoft.Azure.WebJobs.Script.Description;
using Microsoft.Azure.WebJobs.Script.WebHost.Authentication;
using Microsoft.Azure.WebJobs.Script.WebHost.Filters;
using Microsoft.Azure.WebJobs.Script.WebHost.Models;
using Microsoft.Azure.WebJobs.Script.WebHost.Security.Authorization.Policies;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;

namespace Microsoft.Azure.WebJobs.Script.WebHost.Controllers
{
    /// <summary>
    /// Controller responsible for handling all administrative requests, for
    /// example enqueueing function invocations, etc.
    /// </summary>
    public class AdminController : Controller
    {
        private readonly WebScriptHostManager _scriptHostManager;
        private readonly WebHostSettings _webHostSettings;
        private readonly ILogger _logger;
        private readonly IAuthorizationService _authorizationService;

        public AdminController(WebScriptHostManager scriptHostManager, WebHostSettings webHostSettings, ILoggerFactory loggerFactory, IAuthorizationService authorizationService)
        {
            _scriptHostManager = scriptHostManager;
            _webHostSettings = webHostSettings;
            _logger = loggerFactory.CreateLogger(ScriptConstants.LogCategoryAdminController);
            _authorizationService = authorizationService;
        }

        [HttpPost]
        [Route("admin/functions/{name}")]
        [Authorize(Policy = PolicyNames.AdminAuthLevel)]
        [RequiresRunningHost]
        public IActionResult Invoke(string name, [FromBody] FunctionInvocation invocation)
        {
            if (invocation == null)
            {
                return BadRequest();
            }

            FunctionDescriptor function = _scriptHostManager.Instance.GetFunctionOrNull(name);
            if (function == null)
            {
                return NotFound();
            }

            ParameterDescriptor inputParameter = function.Parameters.First(p => p.IsTrigger);
            Dictionary<string, object> arguments = new Dictionary<string, object>()
            {
                { inputParameter.Name, invocation.Input }
            };
            Task.Run(() => _scriptHostManager.Instance.CallAsync(function.Name, arguments));

            return Accepted();
        }

        [HttpGet]
        [Route("admin/functions/{name}/status")]
        [Authorize(Policy = PolicyNames.AdminAuthLevel)]
        [RequiresRunningHost]
        public IActionResult GetFunctionStatus(string name)
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
                    return NotFound();
                }
            }

            return Ok(status);
        }

        [HttpGet]
        [Route("admin/host/status")]
        [Authorize(Policy = PolicyNames.AdminAuthLevelOrInternal)]
        public IActionResult GetHostStatus()
        {
            var status = new HostStatus
            {
                State = _scriptHostManager.State.ToString(),
                Version = ScriptHost.Version,
                VersionDetails = Utility.GetInformationalVersion(typeof(ScriptHost)),
                Id = _scriptHostManager.Instance?.ScriptConfig.HostConfig.HostId
            };

            var lastError = _scriptHostManager.LastError;
            if (lastError != null)
            {
                status.Errors = new Collection<string>();
                status.Errors.Add(Utility.FlattenException(lastError));
            }

            string message = $"Host Status: {JsonConvert.SerializeObject(status, Formatting.Indented)}";
            _logger.LogInformation(message);

            return Ok(status);
        }

        [HttpPost]
        [Route("admin/host/ping")]
        public IActionResult Ping()
        {
            return Ok();
        }

        [HttpPost]
        [Route("admin/host/log")]
        [Authorize(Policy = PolicyNames.AdminAuthLevelOrInternal)]
        public IActionResult Log([FromBody]IEnumerable<HostLogEntry> logEntries)
        {
            if (logEntries == null)
            {
                return BadRequest("An array of log entry objects is expected.");
            }
            foreach (var logEntry in logEntries)
            {
                var traceEvent = new TraceEvent(logEntry.Level, logEntry.Message, logEntry.Source);
                if (!string.IsNullOrEmpty(logEntry.FunctionName))
                {
                    traceEvent.Properties.Add(ScriptConstants.LogPropertyFunctionNameKey, logEntry.FunctionName);
                }

                var logLevel = Utility.ToLogLevel(traceEvent.Level);
                var logData = new Dictionary<string, object>
                {
                    [ScriptConstants.LogPropertySourceKey] = logEntry.Source,
                    [ScriptConstants.LogPropertyFunctionNameKey] = logEntry.FunctionName
                };
                _logger.Log(logLevel, 0, logData, null, (s, e) => logEntry.Message);
            }

            return Ok();
        }

        [HttpPost]
        [Route("admin/host/debug")]
        [Authorize(Policy = PolicyNames.AdminAuthLevel)]
        public IActionResult LaunchDebugger()
        {
            if (_webHostSettings.IsSelfHost)
            {
                // If debugger is already running, this will be a no-op returning true.
                if (Debugger.Launch())
                {
                    return Ok();
                }
                else
                {
                    return StatusCode(StatusCodes.Status409Conflict);
                }
            }

            return StatusCode(StatusCodes.Status501NotImplemented);
        }

        public override void OnActionExecuting(ActionExecutingContext context)
        {
            // For all admin api requests, we'll update the ScriptHost debug timeout
            // For now, we'll enable debug mode on ANY admin requests. Since the Portal interacts through
            // the admin API this is sufficient for identifying when the Portal is connected.
            _scriptHostManager.Instance?.NotifyDebug();

            base.OnActionExecuting(context);
        }

        [HttpGet]
        [HttpPost]
        [Authorize(AuthenticationSchemes = AuthLevelAuthenticationDefaults.AuthenticationScheme)]
        [RequiresRunningHost]
        [Route("runtime/webhooks/{name}/{*extra}")]
        public async Task<IActionResult> ExtensionWebHookHandler(string name, CancellationToken token)
        {
            var provider = _scriptHostManager.BindingWebHookProvider;

            var handler = provider.GetHandlerOrNull(name);
            if (handler != null)
            {
                string keyName = WebJobsSdkExtensionHookProvider.GetKeyName(name);
                var authResult = await _authorizationService.AuthorizeAsync(User, keyName, PolicyNames.SystemAuthLevel);
                if (!authResult.Succeeded)
                {
                    return Unauthorized();
                }

                var requestMessage = new HttpRequestMessageFeature(this.HttpContext).HttpRequestMessage;
                HttpResponseMessage response = await handler.ConvertAsync(requestMessage, token);

                var result = new ObjectResult(response);
                result.Formatters.Add(new HttpResponseMessageOutputFormatter());
                return result;
            }

            return NotFound();
        }
    }
}