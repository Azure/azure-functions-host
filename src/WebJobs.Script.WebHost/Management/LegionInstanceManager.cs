// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Script.Diagnostics;
using Microsoft.Azure.WebJobs.Script.WebHost.Management.LinuxSpecialization;
using Microsoft.Azure.WebJobs.Script.WebHost.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;

namespace Microsoft.Azure.WebJobs.Script.WebHost.Management
{
    public class LegionInstanceManager : LinuxInstanceManager
    {
        public LegionInstanceManager(IHttpClientFactory httpClientFactory, IScriptWebHostEnvironment webHostEnvironment,
            IEnvironment environment, ILogger<LegionInstanceManager> logger, IMetricsLogger metricsLogger, IMeshServiceClient meshServiceClient) : base(httpClientFactory, webHostEnvironment,
            environment, logger, metricsLogger, meshServiceClient) { }

        public override Task<string> SpecializeMSISidecar(HostAssignmentContext context)
        {
            // Legion will take care of MSI Specialization
            return Task.FromResult<string>(null);
        }

        protected override Task<string> DownloadWarmupAsync(RunFromPackageContext context)
        {
            return Task.FromResult<string>(null);
        }

        public override Task<string> ValidateContext(HostAssignmentContext assignmentContext)
        {
            // Don't need to validate RunFromPackageContext in Legion
            return Task.FromResult<string>(null);
        }

        protected override Task<string> ApplyContextAsync(HostAssignmentContext assignmentContext)
        {
            return Task.FromResult<string>(null);
        }
    }
}
