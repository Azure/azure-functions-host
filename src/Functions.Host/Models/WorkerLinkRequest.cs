// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
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
    /// Omitted identities retain the conservative Starting/Unlinked signal.
    /// </summary>
    [JsonProperty("runtimeIdentity", NullValueHandling = NullValueHandling.Ignore)]
    [JsonConverter(typeof(WorkerLinkRuntimeIdentityConverter))]
    public WorkerLinkRuntimeIdentity? RuntimeIdentity { get; set; }

    /// <summary>
    /// Gets or sets the target WorkerProxy's container encryption key. Never log or return this value.
    /// </summary>
    [JsonProperty("runtimeAccessKey")]
    public string? RuntimeAccessKey { get; set; }

    /// <summary>
    /// Gets or sets the reserved worker-pod container encryption key. It is not used to authenticate
    /// the outbound connection and must not be logged or returned to the caller.
    /// </summary>
    [JsonProperty("workerContainerEncryptionKey")]
    public string? WorkerContainerEncryptionKey { get; set; }
}

/// <summary>
/// Identifies the Runtime pod linking to a WorkerProxy.
/// </summary>
public sealed class WorkerLinkRuntimeIdentity
{
    /// <summary>
    /// Gets or sets the namespace containing the Runtime pod.
    /// </summary>
    [JsonProperty("resourceNamespace")]
    public string? ResourceNamespace { get; set; }

    /// <summary>
    /// Gets or sets the Runtime pod name.
    /// </summary>
    [JsonProperty("podName")]
    public string? PodName { get; set; }

    /// <summary>
    /// Gets or sets the function app associated with the Runtime pod.
    /// </summary>
    [JsonProperty("app")]
    public WorkerLinkFunctionAppIdentity? App { get; set; }

    /// <summary>
    /// Gets or sets additional platform identity fields.
    /// </summary>
    [JsonExtensionData]
    public IDictionary<string, JToken>? AdditionalProperties { get; set; }
}

/// <summary>
/// Identifies the function app associated with a Runtime pod.
/// </summary>
public sealed class WorkerLinkFunctionAppIdentity
{
    /// <summary>
    /// Gets or sets the function app name.
    /// </summary>
    [JsonProperty("functionAppName")]
    public string? FunctionAppName { get; set; }

    /// <summary>
    /// Gets or sets the platform site identifier.
    /// </summary>
    [JsonProperty("functionAppSiteId")]
    public int? FunctionAppSiteId { get; set; }

    /// <summary>
    /// Gets or sets the Antares stamp hosting the function app.
    /// </summary>
    [JsonProperty("antaresStampName")]
    public string? AntaresStampName { get; set; }

    /// <summary>
    /// Gets or sets additional platform app-identity fields.
    /// </summary>
    [JsonExtensionData]
    public IDictionary<string, JToken>? AdditionalProperties { get; set; }
}

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
        if (token.Type == JTokenType.String)
        {
            string value = token.Value<string>()!;
            try
            {
                token = JToken.Parse(value);
            }
            catch (JsonReaderException exception)
            {
                throw new JsonSerializationException("Runtime identity must contain a JSON object.", exception);
            }
        }

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
