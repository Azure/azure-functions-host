// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Text.Json.Serialization;

namespace Azure.Functions.WorkerProxy.Management;

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    PropertyNameCaseInsensitive = true,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(WorkerAssignRequest))]
[JsonSerializable(typeof(InstanceStatePollRequest))]
[JsonSerializable(typeof(WorkerInstanceState))]
[JsonSerializable(typeof(WorkerApiErrorResponse))]
[JsonSerializable(typeof(RequestValidationResponse))]
internal sealed partial class WorkerProxyJsonContext : JsonSerializerContext;
