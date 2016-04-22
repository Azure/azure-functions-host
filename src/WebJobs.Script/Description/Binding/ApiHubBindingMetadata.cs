// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using Microsoft.Azure.ApiHub;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace Microsoft.Azure.WebJobs.Script.Description
{
    public class ApiHubBindingMetadata : BindingMetadata
    {
        [AllowNameResolution]
        public string Path { get; set; }

        public string Key { get; set; }

        public int PollIntervalInSeconds { get; set; }

        [JsonConverter(typeof(StringEnumConverter))]
        public FileWatcherType FileWatcherType { get; set; }

        public override void ApplyToConfig(JobHostConfigurationBuilder configBuilder)
        {
            if (configBuilder == null)
            {
                throw new ArgumentNullException("configBuilder");
            }

            var apiHubConfig = configBuilder.ApiHubConfiguration;

            string connectionString = null;
            if (!string.IsNullOrEmpty(this.Connection))
            {
                connectionString = Utility.GetAppSettingOrEnvironmentValue(Connection);
            }

            apiHubConfig.AddKeyPath(this.Key, connectionString);
        }
    }
}
