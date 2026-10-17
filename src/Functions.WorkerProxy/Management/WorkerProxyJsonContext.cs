// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Text.Json.Serialization;
using Azure.Functions.WorkerProxy.State;

namespace Azure.Functions.WorkerProxy.Management;

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    PropertyNameCaseInsensitive = true,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(WorkerAssignRequest))]
[JsonSerializable(typeof(WorkerInstanceState))]
[JsonSerializable(typeof(SplitWorkerOriginState))]
[JsonSerializable(typeof(WorkerApiErrorResponse))]
[JsonSerializable(typeof(RequestValidationResponse))]
internal sealed partial class WorkerProxyJsonContext : JsonSerializerContext;
