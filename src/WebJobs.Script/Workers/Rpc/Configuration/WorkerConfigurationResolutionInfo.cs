// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Collections.Generic;

namespace Microsoft.Azure.WebJobs.Script.Workers.Rpc.Configuration
{
    internal record struct WorkerConfigurationResolutionInfo(string WorkersDirectoryPath, IReadOnlyList<string> WorkerConfigPathsList)
    {
        public readonly string WorkersDirPath => WorkersDirectoryPath;

        public readonly IReadOnlyList<string> WorkerConfigPaths => WorkerConfigPathsList;
    }
}
