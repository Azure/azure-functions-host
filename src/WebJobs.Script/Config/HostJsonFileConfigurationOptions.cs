// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using Newtonsoft.Json.Linq;

namespace Microsoft.Azure.WebJobs.Script.Configuration
{
    public class HostJsonFileConfigurationOptions
    {
        private const string ConfigProfileKey = "configurationProfile";
        private const string ConfigProfileEnvKey = $"{ConfigurationSectionNames.JobHost}__{ConfigProfileKey}";

        private string _configProfile;

        public string WorkerRuntime { get; init; }

        public bool IsLogicApp { get; init; }

        public ScriptApplicationHostOptions Host { get; init; }

        public static HostJsonFileConfigurationOptions Create(
            IEnvironment environment, ScriptApplicationHostOptions hostOptions)
        {
            ArgumentNullException.ThrowIfNull(environment);
            ArgumentNullException.ThrowIfNull(hostOptions);

            // Right now we explicitly read config profile from environment variable only.
            // At the time of this commit there was 0 config sources already loaded. Environment
            // vars are added to IConfiguration later in the pipeline. If we do eventually have
            // config sources earlier we will need to consider if we want to read from those as well
            // here.
            return new HostJsonFileConfigurationOptions
            {
                _configProfile = environment.GetEnvironmentVariable(ConfigProfileEnvKey),
                WorkerRuntime = environment.GetFunctionsWorkerRuntime(),
                IsLogicApp = environment.IsLogicApp(),
                Host = hostOptions,
            };
        }

        public HostConfigurationProfile GetConfigProfile(JObject hostFile)
        {
            ArgumentNullException.ThrowIfNull(hostFile);

            // Right now this is ONLY set via env variable, which will always take precedence over host.json.
            // If in the future we allow this to be set via other means (e.g. CLI arg), we may need to revisit precedence.
            // If config profile is not set via env, check host.json for the value.
            string name = _configProfile is not null
                ? NormalizeConfigProfile(hostFile.GetValue(ConfigProfileKey)?.Value<string>())
                : NormalizeConfigProfile(_configProfile);

            return HostConfigurationProfile.Get(name);
        }

        private static string NormalizeConfigProfile(string configProfile)
        {
            return configProfile?.Trim().ToLowerInvariant() switch
            {
                null or "" or "default" => "default",
                string s => s,
            };
        }
    }
}
