// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Collections.Generic;

namespace Microsoft.Azure.WebJobs.Script.Workers.Rpc.Configuration
{
    internal record WorkerConfigurationInfo(string WorkersRootDirPath, IReadOnlyList<string> WorkerConfigPaths);
}
