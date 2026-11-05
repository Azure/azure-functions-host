// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using Azure.Functions.Rpc.Client;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Azure.Functions.Host.WorkerLink;

/// <summary>
/// Request body for linking a worker through <c>PUT /admin/workers/{workerId}</c>.
/// </summary>
public sealed class WorkerLinkRequest
{
    /// <summary>
    /// Gets or sets the required HTTP or HTTPS authority of WorkerProxy's runtime-facing
    /// gRPC listener. The endpoint must be reachable from this runtime.
    /// </summary>
    [JsonProperty("workerGrpcEndpoint")]
    public string? WorkerGrpcEndpoint { get; set; }

    /// <summary>
    /// Gets or sets the platform's Runtime identity for Worker-state publication.
    /// The identity must be a nested JSON object; serialized JSON strings are not accepted.
    /// Omitted or null identities retain the conservative Starting/Unlinked signal.
    /// Unknown identity fields are ignored.
    /// </summary>
    [JsonProperty("runtimeIdentity", NullValueHandling = NullValueHandling.Ignore)]
    [JsonConverter(typeof(WorkerLinkRuntimeIdentityConverter))]
    public WorkerLinkRuntimeIdentity? RuntimeIdentity { get; set; }

    /// <summary>
    /// Gets or sets the reserved worker-pod container encryption key. It is not used to authenticate
    /// the outbound connection and must not be logged or returned to the caller.
    /// </summary>
    [JsonProperty("workerContainerEncryptionKey")]
    public string? WorkerContainerEncryptionKey { get; set; }
}

// Newtonsoft otherwise coerces primitive JSON values into the declared identity field types.
internal sealed class WorkerLinkRuntimeIdentityConverter : JsonConverter<WorkerLinkRuntimeIdentity>
{
    public override bool CanWrite => false;

    public override WorkerLinkRuntimeIdentity? ReadJson(JsonReader reader, Type objectType,
        WorkerLinkRuntimeIdentity? existingValue, bool hasExistingValue, JsonSerializer serializer)
    {
        if (reader.TokenType == JsonToken.Null)
        {
            return null;
        }

        JToken token = JToken.Load(reader);
        if (token is not JObject identity)
        {
            throw new JsonSerializationException("Runtime identity must be a JSON object.");
        }

        ValidatePropertyType(identity, "resourceNamespace", JTokenType.String);
        ValidatePropertyType(identity, "podName", JTokenType.String);
        ValidatePropertyType(identity, "app", JTokenType.Object);

        foreach (JProperty property in identity.Properties())
        {
            if (string.Equals(property.Name, "app", StringComparison.OrdinalIgnoreCase) &&
                property.Value is JObject app)
            {
                ValidatePropertyType(app, "functionAppName", JTokenType.String);
                ValidatePropertyType(app, "functionAppSiteId", JTokenType.Integer);
                ValidatePropertyType(app, "antaresStampName", JTokenType.String);
            }
        }

        return identity.ToObject<WorkerLinkRuntimeIdentity>(serializer);
    }

    public override void WriteJson(JsonWriter writer, WorkerLinkRuntimeIdentity? value, JsonSerializer serializer)
    {
        throw new NotSupportedException();
    }

    private static void ValidatePropertyType(JObject value, string propertyName, JTokenType expectedType)
    {
        foreach (JProperty property in value.Properties())
        {
            if (string.Equals(property.Name, propertyName, StringComparison.OrdinalIgnoreCase) &&
                property.Value.Type is not JTokenType.Null &&
                property.Value.Type != expectedType)
            {
                throw new JsonSerializationException(
                    $"Runtime identity property '{propertyName}' must be {expectedType} or null.");
            }
        }
    }
}
