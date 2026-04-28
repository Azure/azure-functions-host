// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Text;
using System.Text.Json;

namespace Microsoft.Azure.Functions.WorkerProxy;

internal static class CapabilityLogFormatter
{
    private const string HostConfigurationJsonCapability = "host_configuration_json";

    public static string Format(IEnumerable<KeyValuePair<string, string>>? capabilities)
    {
        List<KeyValuePair<string, string>> orderedCapabilities = capabilities?
            .OrderBy(static capability => capability.Key, StringComparer.Ordinal)
            .ToList() ?? [];

        if (orderedCapabilities.Count == 0)
        {
            return "{}";
        }

        using var stream = new MemoryStream();
        using var writer = new Utf8JsonWriter(stream);

        writer.WriteStartObject();

        foreach (KeyValuePair<string, string> capability in orderedCapabilities)
        {
            writer.WriteString(capability.Key, GetFormattedValue(capability));
        }

        writer.WriteEndObject();
        writer.Flush();

        return Encoding.UTF8.GetString(stream.GetBuffer(), 0, checked((int)stream.Length));
    }

    private static string GetFormattedValue(KeyValuePair<string, string> capability)
    {
        if (string.Equals(capability.Key, HostConfigurationJsonCapability, StringComparison.Ordinal))
        {
            return $"<omitted; length={capability.Value.Length}>";
        }

        return capability.Value;
    }
}
