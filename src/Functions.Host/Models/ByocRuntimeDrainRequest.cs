// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Azure.Functions.Host.Models;

/// <summary>
/// Requests the scheduling model for an irreversible BYOC Runtime drain.
/// </summary>
[JsonConverter(typeof(ByocRuntimeDrainRequestJsonConverter))]
public sealed class ByocRuntimeDrainRequest
{
    /// <summary>
    /// Gets or sets the exact <c>Phased</c> or <c>Immediate</c> drain model.
    /// </summary>
    [JsonProperty("drainModel")]
    public string? DrainModel { get; set; }
}

/// <summary>
/// Confirms the accepted BYOC Runtime drain model and its original start time.
/// </summary>
/// <param name="DrainModel">The active drain model.</param>
/// <param name="AcceptedAtUtc">The immutable first acceptance time.</param>
public sealed record ByocRuntimeDrainResponse(
    [property: JsonProperty("drainModel")] string DrainModel,
    [property: JsonProperty("acceptedAtUtc")] DateTimeOffset AcceptedAtUtc);

/// <summary>
/// Describes a BYOC Runtime drain conflict.
/// </summary>
/// <param name="Code">The stable error code.</param>
/// <param name="Detail">The safe error description.</param>
public sealed record ByocRuntimeDrainError(
    [property: JsonProperty("code")] string Code,
    [property: JsonProperty("detail")] string Detail);

/// <summary>
/// Contains a BYOC Runtime drain error.
/// </summary>
/// <param name="Error">The error.</param>
public sealed record ByocRuntimeDrainErrorResponse(
    [property: JsonProperty("error")] ByocRuntimeDrainError Error);

internal sealed class ByocRuntimeDrainRequestJsonConverter : JsonConverter
{
    private const string DrainModelPropertyName = "drainModel";

    public override bool CanWrite => false;

    public override bool CanConvert(Type objectType)
        => objectType == typeof(ByocRuntimeDrainRequest);

    public override object? ReadJson(
        JsonReader reader,
        Type objectType,
        object? existingValue,
        JsonSerializer serializer)
    {
        ArgumentNullException.ThrowIfNull(reader);
        JObject body = JObject.Load(
            reader,
            new JsonLoadSettings
            {
                DuplicatePropertyNameHandling = DuplicatePropertyNameHandling.Error,
            });

        foreach (JProperty property in body.Properties())
        {
            if (!string.Equals(
                property.Name,
                DrainModelPropertyName,
                StringComparison.Ordinal))
            {
                throw new JsonSerializationException(
                    "The BYOC Runtime drain request accepts only the canonical drainModel property.");
            }
        }

        JProperty? drainModel = body.Property(
            DrainModelPropertyName,
            StringComparison.Ordinal);
        if (drainModel is not null &&
            drainModel.Value.Type is not JTokenType.String)
        {
            throw new JsonSerializationException(
                "The BYOC Runtime drain request drainModel property must be a string.");
        }

        return new ByocRuntimeDrainRequest
        {
            DrainModel = drainModel?.Value.Value<string>(),
        };
    }

    public override void WriteJson(
        JsonWriter writer,
        object? value,
        JsonSerializer serializer)
        => throw new NotSupportedException();
}
