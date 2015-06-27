// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;

namespace Microsoft.Azure.WebJobs.Host.Bindings
{
    /// <summary>
    /// Used to create binding data for a particular type.
    /// </summary>
    internal class BindingDataProvider : IBindingDataProvider
    {
        private readonly Type _type;
        private readonly IReadOnlyDictionary<string, Type> _contract;
        private readonly IEnumerable<PropertyHelper> _propertyHelpers;

        internal BindingDataProvider(Type type, IReadOnlyDictionary<string, Type> contract, IEnumerable<PropertyHelper> propertyHelpers)
        {
            _type = type;
            _contract = contract;
            _propertyHelpers = propertyHelpers;
        }

        internal Type ValueType
        {
            get { return _type; }
        }

        /// <inheritdoc/>
        public IReadOnlyDictionary<string, Type> Contract
        {
            get { return _contract; }
        }

        /// <summary>
        /// Populate binding data with property values retrieved from given value object matching the provided 
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
            foreach (var propertyHelper in _propertyHelpers)
            {
                object propertyValue = propertyHelper.GetValue(value);
                bindingData.Add(propertyHelper.Name, propertyValue);
            }

            return bindingData;
        }

        /// <summary>
        /// Create a data binding provider instance out of a custom data type in form of a contract mapping 
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
            IReadOnlyList<PropertyHelper> bindingDataProperties = PropertyHelper.GetProperties(type);

            if (bindingDataProperties.Count == 0)
            {
                return null;
            }

            Dictionary<string, Type> contract = new Dictionary<string, Type>(StringComparer.OrdinalIgnoreCase);
            foreach (PropertyHelper property in bindingDataProperties)
            {
                contract.Add(property.Name, property.PropertyType);
            }

            return new BindingDataProvider(type, contract, bindingDataProperties);
        }
    }
}
