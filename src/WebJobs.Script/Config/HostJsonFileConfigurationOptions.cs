// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

#nullable enable

using System;
using Newtonsoft.Json.Linq;

namespace Microsoft.Azure.WebJobs.Script.Configuration
{
    public sealed class HostJsonFileConfigurationOptions
    {
        private const string ConfigProfileKey = "configurationProfile";
        private const string ConfigProfileEnvKey = $"{ConfigurationSectionNames.JobHost}__{ConfigProfileKey}";

        public HostJsonFileConfigurationOptions(ScriptApplicationHostOptions hostOptions)
        {
            ArgumentNullException.ThrowIfNull(hostOptions);
            Host = hostOptions;
        }

        public HostJsonFileConfigurationOptions(
            IEnvironment environment, ScriptApplicationHostOptions hostOptions)
            : this(hostOptions)
        {
            ArgumentNullException.ThrowIfNull(environment);
            ArgumentNullException.ThrowIfNull(hostOptions);

            // Right now we explicitly read config profile from environment variable only.
            // At the time of this commit there was 0 config sources already loaded. Environment
            // vars are added to IConfiguration later in the pipeline. If we do eventually have
            // config sources earlier we will need to consider if we want to read from those as well
            // here.
            ConfigProfile = environment.GetEnvironmentVariable(ConfigProfileEnvKey);
            WorkerRuntime = environment.GetEnvironmentVariableOrDefault(EnvironmentSettingNames.FunctionWorkerRuntime, string.Empty);
            IsLogicApp = environment.IsLogicApp();
        }

        public string? ConfigProfile { get; init; }

        public string WorkerRuntime { get; init; } = string.Empty;

        public bool IsLogicApp { get; init; }

        public ScriptApplicationHostOptions Host { get; }

        public HostConfigurationProfile GetConfigProfile(JObject hostFile)
        {
            ArgumentNullException.ThrowIfNull(hostFile);

            // Right now this is ONLY set via env variable, which will always take precedence over host.json.
            // If in the future we allow this to be set via other means (e.g. CLI arg), we may need to revisit precedence.
            // If config profile is not set via env, check host.json for the value.
            string profile = ConfigProfile ?? hostFile.GetValue(ConfigProfileKey)?.Value<string>() ?? string.Empty;

            return HostConfigurationProfile.Get(profile);
        }
    }
}
