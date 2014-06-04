// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.

using System.Collections.Generic;

namespace Microsoft.Azure.Jobs.Host
{
    internal static class ObjectDictionaryConverter
    {
        public static IDictionary<string, object> AsDictionary(object values)
        {
            if (values == null)
            {
                return null;
            }

            IDictionary<string, object> valuesAsDictionary = values as IDictionary<string, object>;

            if (valuesAsDictionary != null)
            {
                return valuesAsDictionary;
            }

            IDictionary<string, object> dictionary = new Dictionary<string, object>();

            foreach (PropertyHelper property in PropertyHelper.GetProperties(values))
            {
                // Extract the property values from the property helper
                // The advantage here is that the property helper caches fast accessors.
                dictionary.Add(property.Name, property.GetValue(values));
            }

            return dictionary;
        }
    }
}
