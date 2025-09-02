// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Text.Json;

namespace Microsoft.Azure.WebJobs.Script.Workers.Rpc.Configuration
{
    internal record WorkerConfigFileInfo(string WorkerConfigPath, JsonElement WorkerConfig, RpcWorkerDescription RpcWorkerDescription);
}
