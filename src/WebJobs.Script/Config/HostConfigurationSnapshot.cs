// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

#nullable enable

using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Configuration.Memory;
using Newtonsoft.Json.Linq;

namespace Microsoft.Azure.WebJobs.Script.Configuration;

/// <summary>
/// An immutable, validated Host configuration that can be reused by successive ScriptHost instances.
/// </summary>
public sealed class HostConfigurationSnapshot : IConfigurationSource
{
    private readonly FrozenDictionary<string, string?> _data;

    private HostConfigurationSnapshot(Dictionary<string, string> data)
    {
        _data = data.ToFrozenDictionary(pair => pair.Key, pair => (string?)pair.Value, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Parses delivered configuration without reading, writing, or logging application content.
    /// </summary>
    /// <param name="content">The decoded JSON payload, or <see langword="null"/> only for explicitly missing configuration.</param>
    /// <param name="configurationProfile">The bootstrap profile, which takes precedence over the payload's profile.</param>
    /// <returns>An immutable source containing the supplied settings and the selected profile, without file-bootstrap defaults.</returns>
    /// <exception cref="HostConfigurationException">The payload is not a strict JSON object or its configuration profile is invalid.</exception>
    public static HostConfigurationSnapshot Parse(string? content, string? configurationProfile = null)
    {
        try
        {
            JObject document = new();
            if (content is not null)
            {
                // Validate wire syntax before reusing Json.NET's existing value conversions.
                using (JsonDocument.Parse(content))
                {
                    document = JObject.Parse(content);
                }
            }

            string profile = configurationProfile ?? document["configurationProfile"]?.Value<string>() ?? string.Empty;

            return new(Flatten(document, HostConfigurationProfile.Get(profile)));
        }
        catch (Exception exception) when (exception is JsonException or Newtonsoft.Json.JsonException or NotSupportedException or
            ArgumentException or InvalidCastException or FormatException)
        {
            // Parser and profile exceptions can contain customer values, paths, or document fragments.
            throw new HostConfigurationException(
                "The Host configuration must be a JSON object with a supported configuration profile.");
        }
    }

    /// <inheritdoc />
    public IConfigurationProvider Build(IConfigurationBuilder builder)
    {
        return new MemoryConfigurationProvider(new MemoryConfigurationSource { InitialData = _data });
    }

    internal static Dictionary<string, string> Flatten(JObject document, HostConfigurationProfile profile)
    {
        Dictionary<string, string> data = new(StringComparer.OrdinalIgnoreCase);
        Stack<string> path = new();
        foreach ((string key, string value) in profile.Configuration)
        {
            data[ConfigurationPath.Combine(ConfigurationSectionNames.JobHost, key)] = value;
        }

        ProcessToken(document);

        return data;

        void ProcessToken(JToken token)
        {
            switch (token.Type)
            {
                case JTokenType.Object:
                    foreach (JProperty property in ((JObject)token).Properties())
                    {
                        path.Push(property.Name);
                        ProcessToken(property.Value);
                        path.Pop();
                    }

                    break;
                case JTokenType.Array:
                    JArray array = (JArray)token;
                    for (int i = 0; i < array.Count; i++)
                    {
                        path.Push(i.ToString(CultureInfo.InvariantCulture));
                        ProcessToken(array[i]);
                        path.Pop();
                    }

                    break;
                case JTokenType.Integer:
                case JTokenType.Float:
                case JTokenType.String:
                case JTokenType.Boolean:
                case JTokenType.Null:
                case JTokenType.Date:
                case JTokenType.Raw:
                case JTokenType.Bytes:
                case JTokenType.TimeSpan:
                    string key = ConfigurationSectionNames.JobHost + ConfigurationPath.KeyDelimiter + ConfigurationPath.Combine(path.Reverse());
                    data[key] = ((JValue)token).ToString(CultureInfo.InvariantCulture);
                    break;
            }
        }
    }
}
