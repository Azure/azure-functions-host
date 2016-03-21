// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using Microsoft.Azure.WebJobs.Extensions.DocumentDB;

namespace Microsoft.Azure.WebJobs.Script.Description
{
    internal class DocumentDBBindingMetadata : BindingMetadata
    {
        public string DatabaseName { get; set; }

        public string CollectionName { get; set; }

        public bool CreateIfNotExists { get; set; }

        public override void ApplyToConfig(JobHostConfigurationBuilder configBuilder)
        {
            if (configBuilder == null)
            {
                throw new ArgumentNullException("configBuilder");
            }

            DocumentDBConfiguration config = new DocumentDBConfiguration();
            if (!string.IsNullOrEmpty(Connection))
            {
                config.ConnectionString = Utility.GetAppSettingOrEnvironmentValue(Connection);
            }

            configBuilder.Config.UseDocumentDB(config);
        }
    }
}
