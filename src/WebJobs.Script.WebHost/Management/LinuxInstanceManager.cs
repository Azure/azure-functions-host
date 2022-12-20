// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Azure.Storage;
using Microsoft.Azure.Storage.File;
using Microsoft.Azure.WebJobs.Script.Diagnostics;
using Microsoft.Azure.WebJobs.Script.WebHost.Configuration;
using Microsoft.Azure.WebJobs.Script.WebHost.Management.LinuxSpecialization;
using Microsoft.Azure.WebJobs.Script.WebHost.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;

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

        public bool StartAssignment(HostAssignmentContext context)
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

            if (context.IsWarmupRequest)
            {
                // Based on profiling download code jit-ing holds up cold start.
                // Pre-jit to avoid paying the cost later.
                Task.Run(async () => await DownloadWarmupAsync(context.GetRunFromPkgContext()));
                return true;
            }
            else if (_assignmentContext == null)
            {
                lock (_assignmentLock)
                {
                    if (_assignmentContext != null)
                    {
                        return _assignmentContext.Equals(context);
                    }
                    _assignmentContext = context;
                }

                _logger.LogInformation($"Starting Assignment. Cloud Name: {_environment.GetCloudName()}");

                // set a flag which will cause any incoming http requests to buffer
                // until specialization is complete
                // the host is guaranteed not to receive any requests until AFTER assign
                // has been initiated, so setting this flag here is sufficient to ensure
                // that any subsequent incoming requests while the assign is in progress
                // will be delayed until complete
                _webHostEnvironment.DelayRequests();

                // start the specialization process in the background
                Task.Run(async () => await AssignAsync(context));

                return true;
            }
            else
            {
                // No lock needed here since _assignmentContext is not null when we are here
                return _assignmentContext.Equals(context);
            }
        }

        public abstract Task<string> ValidateContext(HostAssignmentContext assignmentContext);

        private async Task AssignAsync(HostAssignmentContext assignmentContext)
        {
            try
            {
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
