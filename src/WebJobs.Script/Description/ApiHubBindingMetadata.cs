// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Globalization;
using System.IO;
using System.Threading;
using Microsoft.Azure.WebJobs.Extensions.ApiHub;
using Newtonsoft.Json;

namespace Microsoft.Azure.WebJobs.Script.Description
{
    public class ApiHubBindingMetadata : BindingMetadata
    {
        [AllowNameResolution]
        public string ConnectionString { get; set; }
        
        [AllowNameResolution]
        public string Path { get; set; }

        [JsonIgnore]
        public string Key { get; set; }

        public override void ApplyToConfig(JobHostConfigurationBuilder configBuilder)
        {
            if (configBuilder == null)
            {
                throw new ArgumentNullException("configBuilder");
            }

            var apiHubConfig = configBuilder.ApiHubConfig;

            apiHubConfig.AddKeyPath(this.Key, this.ConnectionString);
        }
    }
}
