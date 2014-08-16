// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Microsoft.Azure.Jobs.Host.Bindings
{
    internal class BindingDataProvider : IBindingDataProvider
    {
        private Type _type;
        private IReadOnlyDictionary<string, Type> _contract;

        public BindingDataProvider(Type type, IReadOnlyDictionary<string, Type> contract)
        {
            _type = type;
            _contract = contract;
        }

        public Type ValueType
        {
            get { return _type; }
        }

        public IReadOnlyDictionary<string, Type> Contract
        {
            get { return _contract; }
        }

        /// <summary>
        /// Create data binding provider instance out of a custom data type in form of a contract mapping 
        /// root level public property names to their types using reflection API.
        /// </summary>
        /// <param name="type">Custom data type</param>
        /// <returns>Instance of a binding data contract or null for unsupported types.</returns>
        public static BindingDataProvider FromType(Type type)
        {
            if ((type == typeof(object)) ||
                (type == typeof(string)) ||
                (type == typeof(byte[])))
            {
                // No binding data is available for primitive types.
                return null;
            }

            // The properties on user-defined types are valid binding data.
            IReadOnlyList<PropertyInfo> bindingDataProperties =
                type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                    .Where(p => ObjectBinderHelpers.UseToStringParser(p.PropertyType))
                    .ToList();

            if (bindingDataProperties.Count == 0)
            {
                return null;
            }

            Dictionary<string, Type> entries = new Dictionary<string, Type>(StringComparer.OrdinalIgnoreCase);
            foreach (PropertyInfo property in bindingDataProperties)
            {
                entries.Add(property.Name, property.PropertyType);
            }

            return new BindingDataProvider(type, entries);
        }

        /// <summary>
        /// Populate binding data with property values retrieved from given value object matching provided 
        /// data binding contract.
        /// </summary>
        /// <param name="value">A value object</param>
        /// <returns>Read-only dictionary of property name to value mappings or null if either contract entries or value object are null.</returns>
        public IReadOnlyDictionary<string, object> GetBindingData(object value)
        {
            if (value != null && value.GetType() != ValueType)
            {
                throw new ArgumentException("Provided value is not of the given type", "value");
            }

            if (Contract == null || value == null)
            {
                return null;
            }

            Dictionary<string, object> bindingData = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
            foreach (var entry in Contract)
            {
                var propertyValue = ValueType.GetProperty(entry.Key).GetValue(value, null);
                bindingData.Add(entry.Key, propertyValue);
            }

            return bindingData;
        }
    }
}
