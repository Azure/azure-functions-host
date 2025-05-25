// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Collections.Generic;

namespace Microsoft.Azure.WebJobs.Script.Workers.Rpc.Configuration
{
    public interface IWorkerConfigurationResolver
    {
        internal List<string> GetWorkerConfigs(List<string> probingPaths, string fallbackPath);
    }
}