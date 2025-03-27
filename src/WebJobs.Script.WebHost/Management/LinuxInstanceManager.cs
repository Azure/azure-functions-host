// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Script.Diagnostics;
using Microsoft.Azure.WebJobs.Script.WebHost.Models;
using Microsoft.Extensions.Logging;

namespace Microsoft.Azure.WebJobs.Script.WebHost.Management
{
    public abstract class LinuxInstanceManager : IInstanceManager
    {
        private const string WebsiteNodeDefaultVersion = "8.5.0";

        private readonly object _assignmentLock = new object();
        private readonly ILogger _logger;
        private readonly IMeshServiceClient _meshServiceClient;
        private readonly IEnvironment _environment;
        private readonly HttpClient _client;
        private readonly IScriptWebHostEnvironment _webHostEnvironment;
        private Task _assignment;

        private HostAssignmentContext _assignmentContext;

        public LinuxInstanceManager(IHttpClientFactory httpClientFactory, IScriptWebHostEnvironment webHostEnvironment,
            IEnvironment environment, ILogger<LinuxInstanceManager> logger, IMetricsLogger metricsLogger, IMeshServiceClient meshServiceClient)
        {
            _client = httpClientFactory?.CreateClient() ?? throw new ArgumentNullException(nameof(httpClientFactory));
            _webHostEnvironment = webHostEnvironment ?? throw new ArgumentNullException(nameof(webHostEnvironment));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _meshServiceClient = meshServiceClient;
            _environment = environment ?? throw new ArgumentNullException(nameof(environment));
        }

        public abstract Task<string> SpecializeMSISidecar(HostAssignmentContext context);

        public async Task<bool> AssignInstanceAsync(HostAssignmentContext context)
        {
            if (!IsValidEnvironment(context))
            {
                return false;
            }

            if (context.IsWarmupRequest)
            {
                await HandleWarmupRequestAsync(context);
                return true;
            }

            lock (_assignmentLock)
            {
                if (_assignmentContext == null)
                {
                    _assignmentContext = context;
                    _assignment = AssignAsync(context);
                }
                else if (!_assignmentContext.Equals(context))
                {
                    return false;
                }
            }

            await _assignment;
            return true;
        }

        public bool StartAssignment(HostAssignmentContext context)
        {
            if (!IsValidEnvironment(context))
            {
                return false;
            }

            if (context.IsWarmupRequest)
            {
                Task.Run(async () => await HandleWarmupRequestAsync(context));
                return true;
            }

            lock (_assignmentLock)
            {
                if (_assignmentContext != null)
                {
                    return _assignmentContext.Equals(context);
                }
                _assignmentContext = context;
                _assignment = AssignAsync(context);
            }

            return true;
        }

        public abstract Task<string> ValidateContext(HostAssignmentContext assignmentContext);

        private bool IsValidEnvironment(HostAssignmentContext context)
        {
            if (!_webHostEnvironment.InStandbyMode)
            {
                // This is only true when specializing pinned containers.
                if (!context.Environment.TryGetValue(EnvironmentSettingNames.ContainerStartContext, out string startContext))
                {
                    _logger.LogError("Assign called while host is not in placeholder mode and start context is not present.");
                    return false;
                }
            }

            if (_environment.IsContainerReady())
            {
                _logger.LogError("Assign called while container is marked as specialized.");
                return false;
            }

            return true;
        }

        private async Task AssignAsync(HostAssignmentContext assignmentContext)
        {
            await Task.Yield(); // This may be called from within a lock. When AssignAsync is awaited, control flow will return to the caller and the lock will be released when it exits the lock scope.

            try
            {
                _logger.LogInformation($"Starting Assignment. Cloud Name: {_environment.GetCloudName()}");

                // set a flag which will cause any incoming http requests to buffer
                // until specialization is complete
                // the host is guaranteed not to receive any requests until AFTER assign
                // has been initiated, so setting this flag here is sufficient to ensure
                // that any subsequent incoming requests while the assign is in progress
                // will be delayed until complete
                _webHostEnvironment.DelayRequests();

                // first make all environment and file system changes required for
                // the host to be specialized
                _logger.LogInformation("Applying {environmentCount} app setting(s)", assignmentContext.Environment.Count);
                assignmentContext.ApplyAppSettings(_environment, _logger);
                await ApplyContextAsync(assignmentContext);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Assign failed");
                await _meshServiceClient.NotifyHealthEvent(ContainerHealthEventType.Fatal, GetType(), "Assign failed");
                throw;
            }
            finally
            {
                // all assignment settings/files have been applied so we can flip
                // the switch now on specialization
                // even if there are failures applying context above, we want to
                // leave placeholder mode
                _logger.LogInformation("Triggering specialization");
                _webHostEnvironment.FlagAsSpecializedAndReady();

                _webHostEnvironment.ResumeRequests();
            }
        }

        private async Task HandleWarmupRequestAsync(HostAssignmentContext assignmentContext)
        {
            try
            {
                await DownloadWarmupAsync(assignmentContext.GetRunFromPkgContext());
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Warmup download failed");
                await _meshServiceClient.NotifyHealthEvent(ContainerHealthEventType.Warning, GetType(), "Warmup download failed");
                throw;
            }
            return;
        }

        protected abstract Task ApplyContextAsync(HostAssignmentContext assignmentContext);

        protected abstract Task<string> DownloadWarmupAsync(RunFromPackageContext context);

        public IDictionary<string, string> GetInstanceInfo()
        {
            return new Dictionary<string, string>
            {
                { EnvironmentSettingNames.FunctionsExtensionVersion, ScriptHost.Version },
                { EnvironmentSettingNames.WebsiteNodeDefaultVersion, WebsiteNodeDefaultVersion }
            };
        }

        // for testing
        internal void Reset()
        {
            _assignmentContext = null;
        }
    }
}
