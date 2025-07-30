// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

namespace Microsoft.Azure.WebJobs.Script.Workers.Rpc.Configuration
{
    public class WorkerConfigurationResolverOptions
    {
        // Gets or sets the workers directory path within the Host or defined by IConfiguration.
        public string WorkersDirPath { get; set; }
    }
}