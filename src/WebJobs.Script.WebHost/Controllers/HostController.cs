// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using DryIoc;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.WebApiCompatShim;
using Microsoft.Azure.WebJobs.Host;
using Microsoft.Azure.WebJobs.Host.Executors;
using Microsoft.Azure.WebJobs.Host.Scale;
using Microsoft.Azure.WebJobs.Script.ExtensionBundle;
using Microsoft.Azure.WebJobs.Script.Scale;
using Microsoft.Azure.WebJobs.Script.WebHost.Filters;
using Microsoft.Azure.WebJobs.Script.WebHost.Management;
using Microsoft.Azure.WebJobs.Script.WebHost.Models;
using Microsoft.Azure.WebJobs.Script.WebHost.Security.Authorization.Policies;
using Microsoft.Extensions.DependencyInjection;
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
        private readonly ILogger _logger;
        private readonly IEnvironment _environment;
        private readonly IScriptHostManager _scriptHostManager;
        private readonly IFunctionsSyncManager _functionsSyncManager;
        private readonly HostPerformanceManager _performanceManager;
        private static int _warmupExecuted;

        public HostController(IOptions<ScriptApplicationHostOptions> applicationHostOptions,
            ILoggerFactory loggerFactory,
            IEnvironment environment,
            IScriptHostManager scriptHostManager,
            IFunctionsSyncManager functionsSyncManager,
            HostPerformanceManager performanceManager)
        {
            _applicationHostOptions = applicationHostOptions;
            _logger = loggerFactory.CreateLogger(ScriptConstants.LogCategoryHostController);
            _environment = environment;
            _scriptHostManager = scriptHostManager;
            _functionsSyncManager = functionsSyncManager;
            _performanceManager = performanceManager;
        }

        [HttpGet]
        [Route("admin/host/status")]
        [Authorize(Policy = PolicyNames.AdminAuthLevelOrInternal)]
        [TypeFilter(typeof(EnableDebugModeFilter))]
        public async Task<IActionResult> GetHostStatus([FromServices] IScriptHostManager scriptHostManager, [FromServices] IHostIdProvider hostIdProvider, [FromServices] IServiceProvider serviceProvider = null)
        {
            var status = new HostStatus
            {
                State = scriptHostManager.State.ToString(),
                Version = ScriptHost.Version,
                VersionDetails = Utility.GetInformationalVersion(typeof(ScriptHost)),
                Id = await hostIdProvider.GetHostIdAsync(CancellationToken.None),
                ProcessUptime = (long)(DateTime.UtcNow - Process.GetCurrentProcess().StartTime).TotalMilliseconds
            };

            var bundleManager = serviceProvider.GetService<IExtensionBundleManager>();
            if (bundleManager != null)
            {
                var bundleInfo = await bundleManager.GetExtensionBundleDetails();
                if (bundleInfo != null)
                {
                    status.ExtensionBundle = new Models.ExtensionBundle()
                    {
                        Id = bundleInfo.Id,
                        Version = bundleInfo.Version
                    };
                }
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
        [Route("admin/host/drain")]
        [Authorize(Policy = PolicyNames.AdminAuthLevelOrInternal)]
        public IActionResult Drain([FromServices] IDrainModeManager drainModeManager)
        {
            _logger.LogInformation("Received request for draining host");

            // Stop call to some listeners get stuck, Not waiting for the stop call to complete
            drainModeManager.EnableDrainModeAsync(CancellationToken.None).ConfigureAwait(false);
            return Ok();
        }

        [HttpGet]
        [HttpPost]
        [Route("admin/host/ping")]
        [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
        public async Task<IActionResult> Ping([FromServices] IScriptHostManager scriptHostManager)
        {
            var result = await _performanceManager.TryHandleHealthPingAsync(HttpContext.Request, _logger);
            if (result != null)
            {
                return result;
            }

            var pingStatus = new JObject
            {
                { "hostState", scriptHostManager.State.ToString() }
            };

            string message = $"Ping Status: {pingStatus.ToString()}";
            _logger.Log(LogLevel.Debug, new EventId(0, "PingStatus"), message);

            return Ok();
        }

        [HttpPost]
        [Route("admin/host/scale/status")]
        [Authorize(Policy = PolicyNames.AdminAuthLevelOrInternal)]
        [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
        [RequiresRunningHost]
        public async Task<IActionResult> GetScaleStatus([FromBody] ScaleStatusContext context, [FromServices] FunctionsScaleManager scaleManager)
        {
            // if runtime scale isn't enabled return error
            if (!_environment.IsRuntimeScaleMonitoringEnabled())
            {
                return BadRequest("Runtime scale monitoring is not enabled.");
            }

            var scaleStatus = await scaleManager.GetScaleStatusAsync(context);

            return new ObjectResult(scaleStatus);
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
                if (_environment.IsFileSystemReadOnly())
                {
                    return BadRequest();
                }

                // we're currently offline and the request is to bring the host back online
                await FileMonitoringService.SetAppOfflineState(_applicationHostOptions.Value.ScriptPath, false);
            }
            else if (desiredState == ScriptHostState.Offline && currentState != ScriptHostState.Offline)
            {
                if (_environment.IsFileSystemReadOnly())
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

        [HttpGet]
        [HttpPost]
        [Route("admin/warmup")]
        [RequiresRunningHost]
        public async Task<IActionResult> Warmup([FromServices] IScriptHostManager scriptHostManager)
        {
            // Endpoint only for Windows Elastic Premium or Linux App Service plans
            if (!(_environment.IsLinuxAppService() || _environment.IsWindowsElasticPremium()))
            {
                return BadRequest("This API is not available for the current hosting plan");
            }

            if (Interlocked.CompareExchange(ref _warmupExecuted, 1, 0) != 0)
            {
                return Ok();
            }

            if (scriptHostManager is IServiceProvider serviceProvider)
            {
                IScriptJobHost jobHost = serviceProvider.GetService<IScriptJobHost>();

                if (jobHost == null)
                {
                    _logger.LogError($"No active host available.");
                    return StatusCode(503);
                }

                await jobHost.TryInvokeWarmupAsync();
                return Ok();
            }

            return BadRequest("This API is not supported by the current hosting environment.");
        }

        [AcceptVerbs("GET", "POST", "DELETE")]
        [Authorize(Policy = PolicyNames.SystemKeyAuthLevel)]
        [Route("runtime/webhooks/{extensionName}/{*extra}")]
        [RequiresRunningHost]
        public async Task<IActionResult> ExtensionWebHookHandler(string extensionName, CancellationToken token, [FromServices] IScriptWebHookProvider provider)
        {
            if (provider.TryGetHandler(extensionName, out HttpHandler handler))
            {
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
