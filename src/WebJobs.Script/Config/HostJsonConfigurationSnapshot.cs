// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

#nullable enable

using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Configuration.Memory;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Microsoft.Azure.WebJobs.Script.Configuration;

/// <summary>
/// An immutable, validated host.json configuration that can be reused by successive ScriptHost instances.
/// </summary>
public sealed class HostJsonConfigurationSnapshot : IConfigurationSource
{
    private readonly FrozenDictionary<string, string?> _data;

    private HostJsonConfigurationSnapshot(Dictionary<string, string> data)
    {
        _data = data.ToFrozenDictionary(pair => pair.Key, pair => (string?)pair.Value, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Parses delivered configuration without reading, writing, or logging application content.
    /// </summary>
    /// <param name="content">The decoded document, or <see langword="null"/> only for an explicitly missing file.</param>
    /// <param name="options">The bootstrap settings used for defaults and configuration profile selection.</param>
    /// <returns>A snapshot with the same normalization and key mapping as local host.json configuration.</returns>
    /// <exception cref="HostConfigurationException">The document, schema version, or configuration profile is invalid.</exception>
    public static HostJsonConfigurationSnapshot Parse(string? content, HostJsonFileConfigurationOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        try
        {
            JObject document = content is null ? CreateDefault(options, includeBundle: true) : JObject.Parse(content);
            if (!document.HasValues)
            {
                document = CreateDefault(options);
            }

            ValidateVersion(document);

            return new(Flatten(document, GetProfile(document, options)));
        }
        catch (Exception exception) when (exception is JsonException or HostConfigurationException or
            ArgumentException or InvalidCastException or FormatException)
        {
            // Parser and profile exceptions can contain customer values, paths, or document fragments.
            throw new HostConfigurationException(
                "The worker host.json is not valid JSON with a supported schema version and configuration profile.");
        }
    }

    /// <inheritdoc />
    public IConfigurationProvider Build(IConfigurationBuilder builder)
    {
        return new MemoryConfigurationProvider(new MemoryConfigurationSource { InitialData = _data });
    }

    internal static JObject CreateDefault(HostJsonFileConfigurationOptions options, bool includeBundle = false)
    {
        JObject document = new()
        {
            ["version"] = "2.0",
            ["isDefaultHostConfig"] = true,
        };

        if (!options.Host.IsFileSystemReadOnly)
        {
            if (string.Equals(options.WorkerRuntime, "powershell", StringComparison.InvariantCultureIgnoreCase))
            {
                document.Add("managedDependency", new JObject { ["Enabled"] = true });
            }

            if (includeBundle)
            {
                document.Add("extensionBundle", new JObject
                {
                    ["id"] = options.IsLogicApp ? ScriptConstants.WorkFlowExtensionBundleId : ScriptConstants.DefaultExtensionBundleId,
                    ["version"] = options.IsLogicApp ? ScriptConstants.LogicAppDefaultExtensionBundleVersion : ScriptConstants.DefaultExtensionBundleVersion,
                });
            }
        }

        return document;
    }

    internal static void ValidateVersion(JObject document)
    {
        string? version = document["version"]?.Value<string>();
        if (string.IsNullOrEmpty(version))
        {
            throw new HostConfigurationException($"The {ScriptConstants.HostMetadataFileName} file is missing the required 'version' property. See https://aka.ms/functions-hostjson for steps to migrate the configuration file.");
        }

        if (!string.Equals(version, "2.0", StringComparison.Ordinal))
        {
            StringBuilder message = new($"'{version}' is an invalid value for {ScriptConstants.HostMetadataFileName} 'version' property. We recommend you set the 'version' property to '2.0'. ");
            if (version.StartsWith("3", StringComparison.OrdinalIgnoreCase))
            {
                message.Append($"This does not correspond to the function runtime version, only to the schema version of the {ScriptConstants.HostMetadataFileName} file. ");
            }

            message.Append($"See https://aka.ms/functions-hostjson for more information on this configuration file.");
            throw new HostConfigurationException(message.ToString());
        }
    }

    internal static HostConfigurationProfile GetProfile(JObject document, HostJsonFileConfigurationOptions options)
    {
        try
        {
            return options.GetConfigProfile(document);
        }
        catch (NotSupportedException exception)
        {
            throw new HostConfigurationException(exception.Message, exception);
        }
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
