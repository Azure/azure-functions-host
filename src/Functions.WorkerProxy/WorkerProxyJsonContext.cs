// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Text.Json.Serialization;

namespace Microsoft.Azure.Functions.WorkerProxy;

[JsonSerializable(typeof(WorkerPodState))]
[JsonSerializable(typeof(WorkerAssignRequest))]
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
internal partial class WorkerProxyJsonContext : JsonSerializerContext
{
}
