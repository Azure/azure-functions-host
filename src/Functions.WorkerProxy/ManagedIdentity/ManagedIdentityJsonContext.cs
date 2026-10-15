// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Text.Json.Serialization;

namespace Azure.Functions.WorkerProxy.ManagedIdentity;

[JsonSourceGenerationOptions(DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(ManagedIdentityTokenHttpResponse))]
[JsonSerializable(typeof(ManagedIdentityErrorResponse))]
internal sealed partial class ManagedIdentityJsonContext : JsonSerializerContext;
