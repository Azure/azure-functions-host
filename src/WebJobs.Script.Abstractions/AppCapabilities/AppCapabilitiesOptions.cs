// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

#nullable enable

using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;

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

        /// <inheritdoc />
        public ICollection<string> Keys => Capabilities.Keys;

        /// <inheritdoc />
        public ICollection<string> Values => Capabilities.Values;

        /// <inheritdoc />
        public int Count => Capabilities.Count;

        /// <inheritdoc />
        public bool IsReadOnly => Capabilities.IsReadOnly;

        /// <inheritdoc />
        public string this[string key]
        {
            get => Capabilities[key];
            set => Capabilities[key] = value;
        }

        /// <inheritdoc />
        public void Add(string key, string value)
        {
            Capabilities.Add(key, value);
        }

        /// <inheritdoc />
        public void Add(KeyValuePair<string, string> item)
        {
            Capabilities.Add(item);
        }

        /// <inheritdoc />
        public void Clear()
        {
            Capabilities.Clear();
        }

        /// <summary>
        /// Determines whether the <see cref="AppCapabilitiesOptions"/> contains the specified key-value pair.
        /// Key comparison is case-insensitive.
        /// </summary>
        public bool Contains(KeyValuePair<string, string> item)
        {
            return Capabilities.Contains(item);
        }

        /// <summary>
        /// Determines whether the <see cref="AppCapabilitiesOptions"/> contains the specified key.
        /// Key comparison is case-insensitive.
        /// </summary>
        public bool ContainsKey(string key)
        {
            return Capabilities.ContainsKey(key);
        }

        /// <inheritdoc />
        public void CopyTo(KeyValuePair<string, string>[] array, int arrayIndex)
        {
            Capabilities.CopyTo(array, arrayIndex);
        }

        /// <inheritdoc />
        public IEnumerator<KeyValuePair<string, string>> GetEnumerator()
        {
            return Capabilities.GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return ((IEnumerable)Capabilities).GetEnumerator();
        }

        /// <inheritdoc />
        public bool Remove(string key)
        {
            return Capabilities.Remove(key);
        }

        /// <inheritdoc />
        public bool Remove(KeyValuePair<string, string> item)
        {
            return Capabilities.Remove(item);
        }

        /// <inheritdoc cref="IDictionary{TKey, TValue}.TryGetValue"/>
        public bool TryGetValue(string key, [MaybeNullWhen(false)] out string value)
        {
            return Capabilities.TryGetValue(key, out value);
        }

        /// <inheritdoc/>
        bool IDictionary<string, string>.TryGetValue(string key, out string value)
        {
            return Capabilities.TryGetValue(key, out value);
        }
    }
}
