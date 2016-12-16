// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace Microsoft.Azure.WebJobs.Script.Extensibility
{
    /// <summary>
    /// Provides context for script bind operations.
    /// </summary>
    public class ScriptBindingContext
    {
        /// <summary>
        /// Constructs a new instance.
        /// </summary>
        /// <param name="bindingMetadata">The metadata for the binding.</param>
        public ScriptBindingContext(JObject bindingMetadata)
        {
            Metadata = bindingMetadata;

            string direction = GetMetadataValue<string>("direction", "in");
            switch (direction.ToLowerInvariant())
            {
                case "in":
                    Access = FileAccess.Read;
                    break;
                case "out":
                    Access = FileAccess.Write;
                    break;
                case "inout":
                    Access = FileAccess.ReadWrite;
                    break;
            }

            Name = GetMetadataValue<string>("name");
            Type = GetMetadataValue<string>("type");
            DataType = GetMetadataValue<string>("datatype");
            Cardinality = GetMetadataValue<string>("cardinality");
            IsTrigger = Type.EndsWith("trigger", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Gets the raw binding metadata.
        /// </summary>
        public JObject Metadata { get; private set; }

        /// <summary>
        /// Gets the binding name.
        /// </summary>
        public string Name { get; private set; }

        /// <summary>
        /// Gets the binding type.
        /// </summary>
        public string Type { get; private set; }

        /// <summary>
        /// Gets the data type for the binding.
        /// </summary>
        public string DataType { get; private set; }
        
        /// <summary>
        /// Gets the cardinality for the binding.
        /// </summary>
        public string Cardinality { get; private set; }

        /// <summary>
        /// Gets the <see cref="FileAccess"/> for this binding.
        /// </summary>
        public FileAccess Access { get; private set; }

        /// <summary>
        /// Gets a value indicating whether this binding is a trigger binding.
        /// </summary>
        public bool IsTrigger { get; private set; }

        /// <summary>
        /// Helper method for retrieving information from <see cref="Metadata"/>.
        /// </summary>
        /// <typeparam name="TEnum">The type of the enum.</typeparam>
        /// <param name="name">The metadata property name.</param>
        /// <param name="defaultValue">Optional default value to use if the value is not present.</param>
        /// <returns></returns>
        public TEnum GetMetadataEnumValue<TEnum>(string name, TEnum defaultValue = default(TEnum)) where TEnum : struct
        {
            string rawValue = GetMetadataValue<string>(name);

            TEnum enumValue = default(TEnum);
            if (!string.IsNullOrEmpty(rawValue))
            {
                if (Enum.TryParse<TEnum>(rawValue, true, out enumValue))
                {
                    return enumValue;
                }
                else
                {
                    throw new FormatException($"Invalid value specified for binding property '{name}' of enum type {PrettyTypeName(typeof(TEnum))}.");
                }
            }

            return defaultValue;
        }

        /// <summary>
        /// Helper method for retrieving information from <see cref="Metadata"/>;
        /// </summary>
        /// <typeparam name="TValue">The type of the value.</typeparam>
        /// <param name="name">The metadata property name.</param>
        /// <param name="defaultValue">Optional default value to use if the value is not present.</param>
        /// <returns></returns>
        public TValue GetMetadataValue<TValue>(string name, TValue defaultValue = default(TValue))
        {
            JToken value = null;
            try
            {
                if (Metadata.TryGetValue(name, StringComparison.OrdinalIgnoreCase, out value))
                {
                    return value.Value<TValue>();
                }
            }
            catch (FormatException e)
            {
                throw new FormatException($"Invalid value specified for binding property '{name}' of type {PrettyTypeName(typeof(TValue))}.", e);
            }

            return defaultValue;
        }

        private static string PrettyTypeName(Type t)
        {
            if (t.IsGenericType)
            {
                return string.Format(
                    "{0}<{1}>",
                    t.Name.Substring(0, t.Name.LastIndexOf("`", StringComparison.Ordinal)),
                    string.Join(", ", t.GetGenericArguments().Select(PrettyTypeName)));
            }

            return t.Name;
        }
    }
}
