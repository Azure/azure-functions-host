// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

#nullable enable

using System;
using System.Collections;
using System.Collections.Generic;

namespace Microsoft.Azure.WebJobs.Script.AppCapabilities
{
    /// <summary>
    /// Represents a collection of application capabilities, allowing for flexible access and management of capability
    /// values as key-value pairs.
    /// </summary>
    /// <remarks>The keys in the dictionary are case-insensitive, enabling developers to access capability
    /// values without concern for key casing.</remarks>
    public sealed class AppCapabilitiesOptions : IDictionary<string, string>
    {
        private IDictionary<string, string> Capabilities { get; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        ICollection<string> IDictionary<string, string>.Keys => Capabilities.Keys;

        ICollection<string> IDictionary<string, string>.Values => Capabilities.Values;

        int ICollection<KeyValuePair<string, string>>.Count => Capabilities.Count;

        bool ICollection<KeyValuePair<string, string>>.IsReadOnly => Capabilities.IsReadOnly;

        string IDictionary<string, string>.this[string key]
        {
            get => Capabilities[key];
            set => Capabilities[key] = value;
        }

        void IDictionary<string, string>.Add(string key, string value)
        {
            Capabilities.Add(key, value);
        }

        void ICollection<KeyValuePair<string, string>>.Add(KeyValuePair<string, string> item)
        {
            Capabilities.Add(item);
        }

        void ICollection<KeyValuePair<string, string>>.Clear()
        {
            Capabilities.Clear();
        }

        bool ICollection<KeyValuePair<string, string>>.Contains(KeyValuePair<string, string> item)
        {
            return Capabilities.Contains(item);
        }

        bool IDictionary<string, string>.ContainsKey(string key)
        {
            return Capabilities.ContainsKey(key);
        }

        void ICollection<KeyValuePair<string, string>>.CopyTo(KeyValuePair<string, string>[] array, int arrayIndex)
        {
            Capabilities.CopyTo(array, arrayIndex);
        }

        IEnumerator<KeyValuePair<string, string>> IEnumerable<KeyValuePair<string, string>>.GetEnumerator()
        {
            return Capabilities.GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return ((IEnumerable)Capabilities).GetEnumerator();
        }

        bool IDictionary<string, string>.Remove(string key)
        {
            return Capabilities.Remove(key);
        }

        bool ICollection<KeyValuePair<string, string>>.Remove(KeyValuePair<string, string> item)
        {
            return Capabilities.Remove(item);
        }

        bool IDictionary<string, string>.TryGetValue(string key, out string value)
        {
            return Capabilities.TryGetValue(key, out value!);
        }
    }
}
