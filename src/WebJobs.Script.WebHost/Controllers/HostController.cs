// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
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
using Microsoft.Azure.WebJobs.Script.Workers.Rpc;
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
    /// example host status, ping, log, etc.
    /// </summary>
    public class HostController : Controller
    {
        private readonly IOptions<ScriptApplicationHostOptions> _applicationHostOptions;
        private readonly ILogger _logger;
        private readonly IEnvironment _environment;
        private readonly IScriptHostManager _scriptHostManager;
        private readonly IFunctionsSyncManager _functionsSyncManager;
        private readonly HostPerformanceManager _performanceManager;
        private static readonly SemaphoreSlim _drainSemaphore = new SemaphoreSlim(1, 1);
        private static readonly SemaphoreSlim _resumeSemaphore = new SemaphoreSlim(1, 1);

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
            // When making changes to HostStatus, ensure that you update the HostStatus class in AAPT-Antares-Websites repo as well.
            var status = new HostStatus
            {
                State = scriptHostManager.State.ToString(),
                Version = ScriptHost.Version,
                VersionDetails = Utility.GetInformationalVersion(typeof(ScriptHost)),
                PlatformVersion = _environment.GetAntaresVersion(),
                InstanceId = _environment.GetInstanceId(),
                ComputerName = _environment.GetAntaresComputerName(),
                Id = await hostIdProvider.GetHostIdAsync(CancellationToken.None),
                ProcessUptime = (long)(DateTime.UtcNow - Process.GetCurrentProcess().StartTime).TotalMilliseconds,
                FunctionAppContentEditingState = Utility.GetFunctionAppContentEditingState(_environment, _applicationHostOptions)
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

        /// <summary>
        ///  Currently, anyone in Reader role can access this information.
        ///  If this API is extended to include any secrets, it will need to be
        ///  locked down to only Contributor roles.
        /// </summary>
        [HttpGet]
        [Route("admin/host/processes")]
        [Authorize(Policy = PolicyNames.AdminAuthLevelOrInternal)]
        public async Task<IActionResult> GetWorkerProcesses([FromServices] IScriptHostManager scriptHostManager)
        {
            if (!Utility.TryGetHostService(scriptHostManager, out IWebHostRpcWorkerChannelManager webHostLanguageWorkerChannelManager))
            {
                return StatusCode(StatusCodes.Status503ServiceUnavailable);
            }

            var hostProcess = Process.GetCurrentProcess();
            List<FunctionProcesses.FunctionProcessInfo> processes = new()
            {
                new FunctionProcesses.FunctionProcessInfo()
                {
                    ProcessId = hostProcess.Id,
                    DebugEngine = RpcWorkerConstants.DotNetCoreDebugEngine,
                    IsEligibleForOpenInBrowser = false,
                    ProcessName = hostProcess.ProcessName
                }
            };

            string workerRuntime = _environment.GetFunctionsWorkerRuntime();

            List<IRpcWorkerChannel> channels = null;
            if (Utility.TryGetHostService(scriptHostManager, out IJobHostRpcWorkerChannelManager jobHostLanguageWorkerChannelManager))
            {
                channels = jobHostLanguageWorkerChannelManager.GetChannels(workerRuntime).ToList();
            }

            var webhostChannelDictionary = webHostLanguageWorkerChannelManager.GetChannels(workerRuntime);

            List<Task<IRpcWorkerChannel>> webHostchannelTasks = new List<Task<IRpcWorkerChannel>>();
            if (webhostChannelDictionary is not null)
            {
                foreach (var pair in webhostChannelDictionary)
                {
                    var workerChannel = pair.Value.Task;
                    webHostchannelTasks.Add(workerChannel);
                }
            }

            var webHostchannels = await Task.WhenAll(webHostchannelTasks);
            channels = channels ?? new List<IRpcWorkerChannel>();
            channels.AddRange(webHostchannels);

            foreach (var channel in channels)
            {
                var processInfo = new FunctionProcesses.FunctionProcessInfo()
                {
                    ProcessId = channel.WorkerProcess.Process.Id,
                    ProcessName = channel.WorkerProcess.Process.ProcessName,
                    DebugEngine = Utility.GetDebugEngineInfo(channel.WorkerConfig, workerRuntime),
                    IsEligibleForOpenInBrowser = false
                };
                processes.Add(processInfo);
            }

            var functionProcesses = new FunctionProcesses()
            {
                Processes = processes
            };

            return Ok(functionProcesses);
        }

        [HttpPost]
        [Route("admin/host/drain")]
        [Authorize(Policy = PolicyNames.AdminAuthLevelOrInternal)]
        public async Task<IActionResult> Drain([FromServices] IScriptHostManager scriptHostManager)
        {
            _logger.LogDebug("Received request to drain the host");

            if (!Utility.TryGetHostService(scriptHostManager, out IDrainModeManager drainModeManager))
            {
                return StatusCode(StatusCodes.Status503ServiceUnavailable);
            }

            await _drainSemaphore.WaitAsync();

            // Stop call to some listeners gets stuck, not waiting for the stop call to complete
            _ = drainModeManager.EnableDrainModeAsync(CancellationToken.None)
                                .ContinueWith(
                                    antecedent =>
                                    {
                                        if (antecedent.Status == TaskStatus.Faulted)
                                        {
                                            _logger.LogError(antecedent.Exception, "Something went wrong invoking drain mode");
                                        }

                                        _drainSemaphore.Release();
                                    });
            return Accepted();
        }

        [HttpGet]
        [Route("admin/host/drain/status")]
        [Authorize(Policy = PolicyNames.AdminAuthLevelOrInternal)]
        public IActionResult DrainStatus([FromServices] IScriptHostManager scriptHostManager)
        {
            if (Utility.TryGetHostService(scriptHostManager, out IFunctionActivityStatusProvider functionActivityStatusProvider) &&
                Utility.TryGetHostService(scriptHostManager, out IDrainModeManager drainModeManager))
            {
                var functionActivityStatus = functionActivityStatusProvider.GetStatus();
                DrainModeState state = DrainModeState.Disabled;
                if (drainModeManager.IsDrainModeEnabled)
                {
                    state = (functionActivityStatus.OutstandingInvocations == 0 && functionActivityStatus.OutstandingRetries == 0)
                            ? DrainModeState.Completed : DrainModeState.InProgress;
                }

                DrainModeStatus status = new DrainModeStatus()
                {
                    State = state,
                    OutstandingInvocations = functionActivityStatus.OutstandingInvocations,
                    OutstandingRetries = functionActivityStatus.OutstandingRetries
                };

                string message = $"Drain Status: {JsonConvert.SerializeObject(state, Formatting.Indented)}, Activity Status: {JsonConvert.SerializeObject(functionActivityStatus, Formatting.Indented)}";
                _logger.LogDebug(message);

                return Ok(status);
            }
            else
            {
                return StatusCode(StatusCodes.Status503ServiceUnavailable);
            }
        }

        [HttpPost]
        [Route("admin/host/resume")]
        [Authorize(Policy = PolicyNames.AdminAuthLevelOrInternal)]
        public async Task<IActionResult> Resume([FromServices] IScriptHostManager scriptHostManager)
        {
            try
            {
                await _resumeSemaphore.WaitAsync();

                ScriptHostState currentState = scriptHostManager.State;

                _logger.LogDebug($"Received request to resume a draining host - host status: {currentState.ToString()}");

                if (currentState != ScriptHostState.Running
                    || !Utility.TryGetHostService(scriptHostManager, out IDrainModeManager drainModeManager))
                {
                    _logger.LogDebug("The host is not in a state where we can resume.");
                    return StatusCode(StatusCodes.Status409Conflict);
                }

                _logger.LogDebug($"Drain mode enabled: {drainModeManager.IsDrainModeEnabled}");

                if (drainModeManager.IsDrainModeEnabled)
                {
                    _logger.LogDebug("Starting a new host");
                    await scriptHostManager.RestartHostAsync();
                }

                var status = new ResumeStatus { State = scriptHostManager.State };
                return Ok(status);
            }
            finally
            {
                _resumeSemaphore.Release();
            }
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
        public async Task<IActionResult> GetScaleStatus([FromBody] ScaleStatusContext context, [FromServices] IScriptHostManager scriptHostManager)
        {
            // if runtime scale isn't enabled return error
            if (!_environment.IsRuntimeScaleMonitoringEnabled())
            {
                return BadRequest("Runtime scale monitoring is not enabled.");
            }

            // TEMP: Once https://github.com/Azure/azure-functions-host/issues/5161 is fixed, we should take
            // IScaleStatusProvider as a parameter.
            if (Utility.TryGetHostService(scriptHostManager, out IScaleStatusProvider scaleStatusProvider))
            {
                var scaleStatus = await scaleStatusProvider.GetScaleStatusAsync(context);
                return new ObjectResult(scaleStatus);
            }
            else
            {
                // This case should never happen. Because this action is marked RequiresRunningHost,
                // it's only invoked when the host is running, and if it's running, we'll have access
                // to the IScaleStatusProvider.
                return StatusCode(StatusCodes.Status503ServiceUnavailable);
            }
        }

        [HttpPost]
        [Route("admin/host/log")]
        [Authorize(Policy = PolicyNames.AdminAuthLevelOrInternal)]
        public IActionResult Log([FromBody] IEnumerable<HostLogEntry> logEntries)
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
                if (_applicationHostOptions.Value.IsFileSystemReadOnly)
                {
                    return BadRequest();
                }

                // we're currently offline and the request is to bring the host back online
                await FileMonitoringService.SetAppOfflineState(_applicationHostOptions.Value.ScriptPath, false);
            }
            else if (desiredState == ScriptHostState.Offline && currentState != ScriptHostState.Offline)
            {
                if (_applicationHostOptions.Value.IsFileSystemReadOnly)
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

        [AcceptVerbs("GET", "POST", "DELETE", "OPTIONS")]
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

        [HttpGet]
        [Route("admin/host/config")]
        [Authorize(Policy = PolicyNames.AdminAuthLevelOrInternal)]
        [RequiresRunningHost]
        public IActionResult GetConfig([FromServices] IScriptHostManager scriptHostManager)
        {
            if (Utility.TryGetHostService(scriptHostManager, out IHostOptionsProvider provider))
            {
                return Ok(provider.GetOptions().ToString());
            }
            else
            {
                return StatusCode(StatusCodes.Status503ServiceUnavailable);
            }
        }
    }
}
