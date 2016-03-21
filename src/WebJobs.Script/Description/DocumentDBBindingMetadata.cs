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

        public string ConnectionString { get; set; }

        public override void ApplyToConfig(JobHostConfigurationBuilder configBuilder)
        {
            if (configBuilder == null)
            {
                throw new ArgumentNullException("configBuilder");
            }

            DocumentDBConfiguration config = new DocumentDBConfiguration();
            if (!string.IsNullOrEmpty(ConnectionString))
            {
                // Directly resolve this. Ideally this would be handled with 
                // [AllowNameResolution]. We can do that when the following
                // issue is resolved:
                // https://github.com/projectkudu/WebJobsPortal/issues/117
                INameResolver resolver = configBuilder.Config.NameResolver;
                config.ConnectionString = resolver.Resolve(ConnectionString);
            }

            configBuilder.Config.UseDocumentDB(config);
        }
    }
}
