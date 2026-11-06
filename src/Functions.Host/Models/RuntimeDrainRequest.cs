// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Azure.Functions.Host.Models;

/// <summary>
/// Requests the scheduling mode for an irreversible Runtime drain.
/// </summary>
[JsonConverter(typeof(RuntimeDrainRequestJsonConverter))]
public sealed class RuntimeDrainRequest
{
    /// <summary>
    /// Gets or sets the exact <c>Phased</c> or <c>Immediate</c> drain mode.
    /// </summary>
    [JsonProperty("drainMode")]
    public string? DrainMode { get; set; }
}

/// <summary>
/// Confirms the accepted Runtime drain mode and its original start time.
/// </summary>
/// <param name="DrainMode">The active drain mode.</param>
/// <param name="AcceptedAtUtc">The immutable first acceptance time.</param>
public sealed record RuntimeDrainResponse(
    [property: JsonProperty("drainMode")] string DrainMode,
    [property: JsonProperty("acceptedAtUtc")] DateTimeOffset AcceptedAtUtc);

internal sealed class RuntimeDrainRequestJsonConverter : JsonConverter
{
    private const string DrainModePropertyName = "drainMode";

    public override bool CanWrite => false;

    public override bool CanConvert(Type objectType)
        => objectType == typeof(RuntimeDrainRequest);

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
                DrainModePropertyName,
                StringComparison.Ordinal))
            {
                throw new JsonSerializationException(
                    "The Runtime drain request accepts only the canonical drainMode property.");
            }
        }

        JProperty? drainMode = body.Property(
            DrainModePropertyName,
            StringComparison.Ordinal);
        if (drainMode is not null &&
            drainMode.Value.Type is not JTokenType.String)
        {
            throw new JsonSerializationException(
                "The Runtime drain request drainMode property must be a string.");
        }

        return new RuntimeDrainRequest
        {
            DrainMode = drainMode?.Value.Value<string>(),
        };
    }

    public override void WriteJson(
        JsonWriter writer,
        object? value,
        JsonSerializer serializer)
        => throw new NotSupportedException();
}
