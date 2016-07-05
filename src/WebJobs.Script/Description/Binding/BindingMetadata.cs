// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Globalization;
using System.Linq;
using Microsoft.Azure.WebJobs.Host;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Linq;

namespace Microsoft.Azure.WebJobs.Script.Description
{
    /// <summary>
    /// Represents the metadata for a function parameter binding.
    /// </summary>
    public class BindingMetadata
    {
        /// <summary>
        /// Gets or sets the name of the binding.
        /// </summary>
        public string Name { get; set; }

        /// <summary>
        /// Gets or sets an app setting name for a setting whose value
        /// contains the connection details that should be used for this binding.
        /// E.g. for a Queue binding, this might be set to the name of a an app
        /// setting containing the Azure Storage connection string to use.
        /// </summary>
        public string Connection { get; set; }

        /// <summary>
        /// Gets or sets the type of the binding.
        /// </summary>
        public string Type { get; set; }

        /// <summary>
        /// Gets or sets the direction of the binding.
        /// </summary>
        [JsonConverter(typeof(StringEnumConverter))]
        public BindingDirection Direction { get; set; }

        [JsonConverter(typeof(StringEnumConverter))]
        public DataType? DataType { get; set; }

        /// <summary>
        /// Gets a value indicating whether this binding is a trigger binding.
        /// </summary>
        public bool IsTrigger
        {
            get
            {
                return Type.EndsWith("trigger", StringComparison.OrdinalIgnoreCase);
            }
        }

        /// <summary>
        /// Gets the raw binding metadata (after name resolution has been applied
        /// to all values).
        /// </summary>
        public JObject Raw { get; set; }

        /// <summary>
        /// Creates an instance from the specified raw metadata.
        /// </summary>
        /// <param name="raw">The raw binding metadata.</param>
        /// <param name="nameResolver">Optional name resolver to use on properties.</param>
        /// <returns>The new <see cref="BindingMetadata"/> instance.</returns>
        public static BindingMetadata Create(JObject raw, INameResolver nameResolver = null)
        {
            BindingMetadata bindingMetadata = null;
            string bindingDirectionValue = (string)raw["direction"];
            string connection = (string)raw["connection"];
            string bindingType = (string)raw["type"];
            BindingDirection bindingDirection = default(BindingDirection);

            if (!string.IsNullOrEmpty(bindingDirectionValue) &&
                !Enum.TryParse<BindingDirection>(bindingDirectionValue, true, out bindingDirection))
            {
                throw new FormatException(string.Format(CultureInfo.InvariantCulture, "'{0}' is not a valid binding direction.", bindingDirectionValue));
            }

            // TODO: Validate the binding type somehow?

            if (!string.IsNullOrEmpty(connection) &&
                string.IsNullOrEmpty(Utility.GetAppSettingOrEnvironmentValue(connection)))
            {
                throw new FormatException("Invalid Connection value specified.");
            }

            switch (bindingType.ToLowerInvariant())
            {
                case "httptrigger":
                    bindingMetadata = raw.ToObject<HttpTriggerBindingMetadata>();
                    break;
                case "http":
                    bindingMetadata = raw.ToObject<HttpBindingMetadata>();
                    break;
                case "table":
                    bindingMetadata = raw.ToObject<TableBindingMetadata>();
                    break;
                case "manualtrigger":
                    bindingMetadata = raw.ToObject<BindingMetadata>();
                    break;
                default:
                    bindingMetadata = raw.ToObject<BindingMetadata>();
                    break;
            }

            bindingMetadata.Type = bindingType;
            bindingMetadata.Direction = bindingDirection;
            bindingMetadata.Connection = connection;

            if (nameResolver != null)
            {
                nameResolver.ResolveAllProperties(bindingMetadata);

                // We want to pass resolved metadata values into
                // binding extensions, so we resolve fully here
                JObject resolved = new JObject(raw);
                foreach (JProperty property in resolved.Properties().ToArray())
                {
                    if (property.Value != null &&
                        property.Value.Type == JTokenType.String)
                    {
                        string val = (string)property.Value;
                        string newVal = nameResolver.ResolveWholeString(val);
                        resolved[property.Name] = newVal;
                    }
                }
                bindingMetadata.Raw = resolved;
            }
            else
            {
                bindingMetadata.Raw = raw;
            }

            return bindingMetadata;
        }
    }
}
