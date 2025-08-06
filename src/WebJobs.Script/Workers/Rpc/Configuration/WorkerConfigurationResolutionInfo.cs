// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

namespace Microsoft.Azure.WebJobs.Script.Workers.Rpc.Configuration
{
    internal record struct WorkerConfigurationResolutionInfo(string WorkersDirectoryPath)
    {
        public readonly string WorkersDirPath => WorkersDirectoryPath;
    }
}
