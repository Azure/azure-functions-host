// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
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
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.Azure.WebJobs.Host;
using Microsoft.Azure.WebJobs.Host.Executors;
using Microsoft.Azure.WebJobs.Script.ExtensionBundle;
using Microsoft.Azure.WebJobs.Script.WebHost.Authentication;
using Microsoft.Azure.WebJobs.Script.WebHost.Filters;
using Microsoft.Azure.WebJobs.Script.WebHost.Management;
using Microsoft.Azure.WebJobs.Script.WebHost.Models;
using Microsoft.Azure.WebJobs.Script.WebHost.Security;
using Microsoft.Azure.WebJobs.Script.WebHost.Security.Authorization;
using Microsoft.Azure.WebJobs.Script.WebHost.Security.Authorization.Policies;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using HttpHandler = Microsoft.Azure.WebJobs.IAsyncConverter<System.Net.Http.HttpRequestMessage, System.Net.Http.HttpResponseMessage>;

namespace Microsoft.Azure.WebJobs.Script.WebHost.Controllers
{
    /// <summary>
    /// Controller responsible for handling all administrative requests for host operations
    /// example host status, ping, log, etc
    /// </summary>
    public class HostController : Controller
    {
        private readonly IOptions<ScriptApplicationHostOptions> _applicationHostOptions;
        private readonly IOptions<JobHostOptions> _hostOptions;
        private readonly ILogger _logger;
        private readonly IAuthorizationService _authorizationService;
        private readonly IWebFunctionsManager _functionsManager;
        private readonly IEnvironment _environment;
        private readonly IScriptHostManager _scriptHostManager;
        private readonly IFunctionsSyncManager _functionsSyncManager;
        private readonly IExtensionBundleManager _extensionBundleManager;

        public HostController(IOptions<ScriptApplicationHostOptions> applicationHostOptions,
            IOptions<JobHostOptions> hostOptions,
            ILoggerFactory loggerFactory,
            IAuthorizationService authorizationService,
            IWebFunctionsManager functionsManager,
            IEnvironment environment,
            IScriptHostManager scriptHostManager,
            IFunctionsSyncManager functionsSyncManager,
            IExtensionBundleManager extensionBundleManager)
        {
            _applicationHostOptions = applicationHostOptions;
            _hostOptions = hostOptions;
            _logger = loggerFactory.CreateLogger(ScriptConstants.LogCategoryHostController);
            _authorizationService = authorizationService;
            _functionsManager = functionsManager;
            _environment = environment;
            _scriptHostManager = scriptHostManager;
            _functionsSyncManager = functionsSyncManager;
            _extensionBundleManager = extensionBundleManager;
        }

        [HttpGet]
        [Route("admin/host/status")]
        [Authorize(Policy = PolicyNames.AdminAuthLevelOrInternal)]
        [TypeFilter(typeof(EnableDebugModeFilter))]
        public async Task<IActionResult> GetHostStatus([FromServices] IScriptHostManager scriptHostManager, [FromServices] IHostIdProvider hostIdProvider)
        {
            var status = new HostStatus
            {
                State = scriptHostManager.State.ToString(),
                Version = ScriptHost.Version,
                VersionDetails = Utility.GetInformationalVersion(typeof(ScriptHost)),
                Id = await hostIdProvider.GetHostIdAsync(CancellationToken.None),
                ProcessUptime = (long)(DateTime.UtcNow - Process.GetCurrentProcess().StartTime).TotalMilliseconds
            };

            var bundleInfo = await _extensionBundleManager.GetExtensionBundleDetails();
            if (bundleInfo != null)
            {
                status.ExtensionBundle = new Models.ExtensionBundle()
                {
                    Id = bundleInfo.Id,
                    Version = bundleInfo.Version
                };
            }

            var lastError = scriptHostManager.LastError;
            if (lastError != null)
            {
                status.Errors = new Collection<string>();
                status.Errors.Add(Utility.FlattenException(lastError));
            }

            string message = $"Host Status: {JsonConvert.SerializeObject(status, Formatting.Indented)}";
            _logger.LogInformation(message);

            return Ok(status);
        }

        [HttpGet]
        [HttpPost]
        [Route("admin/host/ping")]
        [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
        public IActionResult Ping([FromServices] IScriptHostManager scriptHostManager)
        {
            var pingStatus = new JObject
            {
                { "hostState", scriptHostManager.State.ToString() }
            };

            string message = $"Ping Status: {pingStatus.ToString()}";
            _logger.Log(LogLevel.Debug, new EventId(0, "PingStatus"), message);

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
        [TypeFilter(typeof(EnableDebugModeFilter))]
        public IActionResult LaunchDebugger()
        {
            if (_applicationHostOptions.Value.IsSelfHost)
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
        [Authorize(Policy = PolicyNames.AdminAuthLevelOrInternal)]
        public async Task<IActionResult> SyncTriggers()
        {
            var result = await _functionsSyncManager.TrySyncTriggersAsync();

            // Return a dummy body to make it valid in ARM template action evaluation
            return result.Success
                ? Ok(new { status = "success" })
                : StatusCode(StatusCodes.Status500InternalServerError, new { status = result.Error });
        }

        [HttpPost]
        [Route("admin/host/restart")]
        [Authorize(Policy = PolicyNames.AdminAuthLevel)]
        public IActionResult Restart([FromServices] IScriptHostManager hostManager)
        {
            Task ignore = hostManager.RestartHostAsync();
            return Ok(_applicationHostOptions.Value);
        }

        /// <summary>
        /// Currently this endpoint only supports taking the host offline and bringing it back online.
        /// </summary>
        /// <param name="state">The desired host state. See <see cref="ScriptHostState"/>.</param>
        [HttpPut]
        [Route("admin/host/state")]
        [Authorize(Policy = PolicyNames.AdminAuthLevel)]
        public async Task<IActionResult> SetState([FromBody] string state)
        {
            if (!Enum.TryParse<ScriptHostState>(state, ignoreCase: true, out ScriptHostState desiredState) ||
                !(desiredState == ScriptHostState.Offline || desiredState == ScriptHostState.Running))
            {
                // currently we only allow states Offline and Running
                return BadRequest();
            }

            var currentState = _scriptHostManager.State;
            if (desiredState == currentState)
            {
                return Ok();
            }
            else if (desiredState == ScriptHostState.Running && currentState == ScriptHostState.Offline)
            {
                if (_environment.FileSystemIsReadOnly())
                {
                    return BadRequest();
                }

                // we're currently offline and the request is to bring the host back online
                await FileMonitoringService.SetAppOfflineState(_applicationHostOptions.Value.ScriptPath, false);
            }
            else if (desiredState == ScriptHostState.Offline && currentState != ScriptHostState.Offline)
            {
                if (_environment.FileSystemIsReadOnly())
                {
                    return BadRequest();
                }

                // we're currently online and the request is to take the host offline
                await FileMonitoringService.SetAppOfflineState(_applicationHostOptions.Value.ScriptPath, true);
            }
            else
            {
                return BadRequest();
            }

            return Accepted();
        }

        /// <summary>
        /// This endpoint generates a temporary x-ms-site-restricted-token for core tool
        /// to access KuduLite zipdeploy endpoint in Linux Consumption
        /// </summary>
        /// <returns>
        /// 200 on token generated
        /// 400 on non-Linux container environment
        /// </returns>
        [HttpGet]
        [Route("admin/host/token")]
        [Authorize(Policy = PolicyNames.AdminAuthLevel)]
        public IActionResult GetAdminToken()
        {
            if (!_environment.IsLinuxContainerEnvironment())
            {
                return BadRequest("Endpoint is only available when running in Linux Container");
            }

            string requestHeaderToken = SimpleWebTokenHelper.CreateToken(DateTime.UtcNow.AddMinutes(5));
            return Ok(requestHeaderToken);
        }

        [AcceptVerbs("GET", "POST", "DELETE")]
        [Authorize(AuthenticationSchemes = AuthLevelAuthenticationDefaults.AuthenticationScheme)]
        [Route("runtime/webhooks/{name}/{*extra}")]
        [RequiresRunningHost]
        public async Task<IActionResult> ExtensionWebHookHandler(string name, CancellationToken token, [FromServices] IScriptWebHookProvider provider)
        {
            if (provider.TryGetHandler(name, out HttpHandler handler))
            {
                // must either be authorized at the admin level, or system level with
                // a matching key name
                string keyName = DefaultScriptWebHookProvider.GetKeyName(name);
                if (!AuthUtility.PrincipalHasAuthLevelClaim(User, AuthorizationLevel.System, keyName))
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
