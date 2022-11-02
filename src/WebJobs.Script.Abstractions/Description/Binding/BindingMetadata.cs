// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Globalization;
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
        private const string _systemReturnParameterBindingName = "$return";

        public BindingMetadata() : this(new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase))
        {
        }

        [JsonConstructor]
        private BindingMetadata(Dictionary<string, object> properties)
        {
            if (properties is null)
            {
                Properties = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
            }
            else
            {
                Properties = properties.Count > 0
                                ? new Dictionary<string, object>(properties, StringComparer.OrdinalIgnoreCase)
                                : properties;
            }
        }

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
        /// Gets or sets the binding properties.
        /// </summary>
        public IDictionary<string, object> Properties { get; }

        /// <summary>
        /// Gets or sets the direction of the binding.
        /// </summary>
        [JsonConverter(typeof(StringEnumConverter))]
        public BindingDirection Direction { get; set; }

        [JsonConverter(typeof(StringEnumConverter))]
        public DataType? DataType { get; set; }

        [JsonConverter(typeof(StringEnumConverter))]
        public Cardinality? Cardinality { get; set; }

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

        public bool IsReturn
        {
            get
            {
                return string.Compare(Name, _systemReturnParameterBindingName, StringComparison.OrdinalIgnoreCase) == 0;
            }
        }

        /// <summary>
        /// Gets or sets the raw binding metadata (after name resolution has been applied
        /// to all values).
        /// </summary>
        public JObject Raw { get; set; }

        /// <summary>
        /// Creates an instance from the specified raw metadata.
        /// </summary>
        /// <param name="raw">The raw binding metadata.</param>
        /// <returns>The new <see cref="BindingMetadata"/> instance.</returns>
        public static BindingMetadata Create(JObject raw)
        {
            if (raw is null)
            {
                throw new ArgumentNullException(nameof(raw));
            }

            try
            {
                BindingMetadata bindingMetadata = raw.ToObject<BindingMetadata>();
                bindingMetadata.Raw = raw;

                return bindingMetadata;
            }
            catch (Exception ex)
            {
                string bindingDirectionValue = (string)raw["direction"];
                if (!string.IsNullOrEmpty(bindingDirectionValue) &&
                    !Enum.TryParse<BindingDirection>(bindingDirectionValue, true, out BindingDirection bindingDirection))
                {
                    throw new FormatException(string.Format(CultureInfo.InvariantCulture, "'{0}' is not a valid binding direction.", bindingDirectionValue), ex);
                }

                throw;
            }
        }
    }
}
