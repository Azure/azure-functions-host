// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Text.Json.Serialization;

namespace Azure.Functions.WorkerProxy.ManagedIdentity;

internal sealed record ManagedIdentityTokenHttpResponse(
    [property: JsonPropertyName("access_token")] string AccessToken,
    [property: JsonPropertyName("token_type")] string TokenType,
    [property: JsonPropertyName("expires_on")] string ExpiresOn,
    [property: JsonPropertyName("resource")] string Resource,
    [property: JsonPropertyName("client_id")] string ClientId);
