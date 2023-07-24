// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Host.Executors;
using Microsoft.Azure.WebJobs.Script.Configuration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace Microsoft.Azure.WebJobs.Script
{
    public class ScriptHostIdProvider : IHostIdProvider
    {
        private readonly IConfiguration _config;
        private readonly IEnvironment _environment;
        private readonly IOptionsMonitor<ScriptApplicationHostOptions> _options;
        private readonly HostIdValidator _hostIdValidator;

        public ScriptHostIdProvider(IConfiguration config, IEnvironment environment, IOptionsMonitor<ScriptApplicationHostOptions> options, HostIdValidator hostIdValidator)
        {
            _config = config;
            _environment = environment;
            _options = options;
            _hostIdValidator = hostIdValidator;
        }

        public Task<string> GetHostIdAsync(CancellationToken cancellationToken)
        {
            string hostId = _config[ConfigurationSectionNames.HostIdPath];
            if (hostId == null)
            {
                // if the user hasn't configured an explicit ID, we generate the default ID.
                HostIdResult result = GetDefaultHostId(_environment, _options.CurrentValue);
                hostId = result.HostId;
                if (result.IsTruncated && !result.IsLocal)
                {
                    _hostIdValidator.ScheduleValidation(hostId);
                }
            }

            return Task.FromResult(hostId);
        }

        internal static HostIdResult GetDefaultHostId(IEnvironment environment, ScriptApplicationHostOptions scriptOptions)
        {
            HostIdResult result = new HostIdResult();

            if (environment.IsFlexConsumptionSku())
            {
                // in Flex Consumption, we use a Guid based host ID without truncation.
                string uniqueSlotName = environment?.GetAzureWebsiteUniqueSlotName();
                if (!string.IsNullOrEmpty(uniqueSlotName))
                {
                    byte[] hash;
                    using (MD5 md5 = MD5.Create())
                    {
                        hash = md5.ComputeHash(Encoding.UTF8.GetBytes(uniqueSlotName));
                    }

                    result.HostId = new Guid(hash).ToString().Replace("-", string.Empty).ToLowerInvariant();
                }

                return result;
            }

            // Note: HostIds must conform to the rules documented here: https://learn.microsoft.com/en-us/azure/azure-functions/functions-app-settings#azurefunctionswebhost__hostid
            string hostId = null;
            if (environment.IsAppService() || environment.IsAnyKubernetesEnvironment())
            {
                string uniqueSlotName = environment?.GetAzureWebsiteUniqueSlotName();
                if (!string.IsNullOrEmpty(uniqueSlotName))
                {
                    // If running on Azure Web App, derive the host ID from unique site slot name
                    hostId = uniqueSlotName;
                }
            }
            else if (environment.IsAnyLinuxConsumption())
            {
                // The hostid is derived from the hostname for Linux consumption.
                string hostName = environment.GetEnvironmentVariable(EnvironmentSettingNames.AzureWebsiteHostName);
                hostId = hostName?.Replace(".azurewebsites.net", string.Empty);
            }
            else
            {
                // When running locally, derive a stable host ID from machine name
                // and root path. We use a hash rather than the path itself to ensure
                // IDs differ (due to truncation) between folders that may share the same
                // root path prefix.
                // Note that such an ID won't work in distributed scenarios, so should
                // only be used for local/CLI scenarios.
                string sanitizedMachineName = Environment.MachineName
                    .Where(char.IsLetterOrDigit)
                    .Aggregate(new StringBuilder(), (b, c) => b.Append(c)).ToString();
                hostId = $"{sanitizedMachineName}-{Math.Abs(Utility.GetStableHash(scriptOptions.ScriptPath))}";
                result.IsLocal = true;
            }

            if (!string.IsNullOrEmpty(hostId))
            {
                if (hostId.Length > ScriptConstants.MaximumHostIdLength)
                {
                    // Truncate to the max host name length
                    hostId = hostId.Substring(0, ScriptConstants.MaximumHostIdLength);
                    result.IsTruncated = true;
                }
            }

            // Lowercase and trim any trailing '-' as they can cause problems with queue names
            result.HostId = hostId?.ToLowerInvariant().TrimEnd('-');

            return result;
        }

        public class HostIdResult
        {
            public string HostId { get; set; }

            public bool IsTruncated { get; set; }

            public bool IsLocal { get; set; }
        }
    }
}
