// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.WebApiCompatShim;
using Microsoft.Azure.WebJobs.Host;
using Microsoft.Azure.WebJobs.Script.WebHost.Authentication;
using Microsoft.Azure.WebJobs.Script.WebHost.Filters;
using Microsoft.Azure.WebJobs.Script.WebHost.Management;
using Microsoft.Azure.WebJobs.Script.WebHost.Models;
using Microsoft.Azure.WebJobs.Script.WebHost.Security.Authorization.Policies;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;

namespace Microsoft.Azure.WebJobs.Script.WebHost.Controllers
{
    /// <summary>
    /// Controller responsible for handling all administrative requests for host operations
    /// example host status, ping, log, etc
    /// </summary>
    public class HostController : Controller
    {
        private readonly WebScriptHostManager _scriptHostManager;
        private readonly WebHostSettings _webHostSettings;
        private readonly ILogger _logger;
        private readonly IAuthorizationService _authorizationService;
        private readonly IWebFunctionsManager _functionsManager;

        public HostController(WebScriptHostManager scriptHostManager, WebHostSettings webHostSettings, ILoggerFactory loggerFactory, IAuthorizationService authorizationService, IWebFunctionsManager functionsManager)
        {
            _scriptHostManager = scriptHostManager;
            _webHostSettings = webHostSettings;
            _logger = loggerFactory.CreateLogger(ScriptConstants.LogCategoryHostController);
            _authorizationService = authorizationService;
            _functionsManager = functionsManager;
        }

        [HttpGet]
        [Route("admin/host/status")]
        [Authorize(Policy = PolicyNames.AdminAuthLevelOrInternal)]
        [EnableDebugMode]
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
        [EnableDebugMode]
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

        [HttpPost]
        [Route("admin/host/synctriggers")]
        [Authorize(Policy = PolicyNames.AdminAuthLevel)]
        public async Task<IActionResult> SyncTriggers()
        {
            (var success, var error) = await _functionsManager.TrySyncTriggers();

            // Return a dummy body to make it valid in ARM template action evaluation
            return success
                ? Ok(new { status = "success" })
                : StatusCode(StatusCodes.Status500InternalServerError, new { status = error });
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
