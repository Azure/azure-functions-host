// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Text.Json;
using System.Text.Json.Serialization;
using Azure.Functions.Rpc.Client;

namespace Azure.Functions.Host.Models;

internal sealed class RuntimeDrainRequestJsonConverter : JsonConverter<RuntimeDrainRequest>
{
    private const string DrainModePropertyName = "drainMode";

    public override RuntimeDrainRequest Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using JsonDocument document = JsonDocument.ParseValue(ref reader);
        if (document.RootElement.ValueKind is not JsonValueKind.Object)
        {
            throw new JsonException("The Runtime drain request must be an object.");
        }

        string? value = null;
        bool seenDrainMode = false;
        foreach (JsonProperty property in document.RootElement.EnumerateObject())
        {
            if (!string.Equals(property.Name, DrainModePropertyName, StringComparison.Ordinal) || seenDrainMode)
            {
                throw new JsonException("The Runtime drain request accepts only one canonical drainMode property.");
            }

            if (property.Value.ValueKind is not JsonValueKind.String)
            {
                throw new JsonException("The Runtime drain request drainMode property must be a string.");
            }

            seenDrainMode = true;
            value = property.Value.GetString();
        }

        if (string.IsNullOrWhiteSpace(value))
        {
            return new();
        }

        if (string.Equals(value, nameof(RuntimeDrainMode.Phased), StringComparison.Ordinal))
        {
            return new() { DrainMode = RuntimeDrainMode.Phased };
        }

        if (string.Equals(value, nameof(RuntimeDrainMode.Immediate), StringComparison.Ordinal))
        {
            return new() { DrainMode = RuntimeDrainMode.Immediate };
        }

        // Keep invalid enum text distinct from malformed bodies in the management API error envelope.
        return new() { ValidationError = new(RequestValidationErrorCodes.InvalidValue, DrainModePropertyName) };
    }

    public override void Write(Utf8JsonWriter writer, RuntimeDrainRequest value, JsonSerializerOptions options)
        => throw new NotSupportedException();
}
