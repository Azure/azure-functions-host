// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

#nullable enable

using System;
using System.Collections;
using System.Collections.Generic;

namespace Microsoft.Azure.WebJobs.Script.AppCapabilities
{
    public sealed class AppCapabilitiesOptions : IDictionary<string, string>
    {
        /// <summary>
        /// Maximum number of capabilities that can be stored.
        /// </summary>
        public const int MaxCapabilities = 50;

        /// <summary>
        /// Maximum length of a capability key.
        /// </summary>
        public const int MaxKeyLength = 200;

        /// <summary>
        /// Maximum length of a capability value.
        /// </summary>
        public const int MaxValueLength = 2000;

        /// <summary>
        ///  Gets the capabilities of the current instance, represented as a dictionary of key-value pairs.
        /// </summary>
        /// <remarks>The keys in the dictionary are case-insensitive, allowing for flexible access to
        /// capability values.</remarks>
        private IDictionary<string, string> Capabilities { get; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        ICollection<string> IDictionary<string, string>.Keys => Capabilities.Keys;

        ICollection<string> IDictionary<string, string>.Values => Capabilities.Values;

        int ICollection<KeyValuePair<string, string>>.Count => Capabilities.Count;

        bool ICollection<KeyValuePair<string, string>>.IsReadOnly => Capabilities.IsReadOnly;

        string IDictionary<string, string>.this[string key]
        {
            get => Capabilities[key];
            set
            {
                ValidateKey(key);
                ValidateValue(value);

                if (!Capabilities.ContainsKey(key) && Capabilities.Count >= MaxCapabilities)
                {
                    throw new InvalidOperationException($"Cannot add more than {MaxCapabilities} capabilities.");
                }

                Capabilities[key] = value;
            }
        }

        void IDictionary<string, string>.Add(string key, string value)
        {
            ValidateKey(key);
            ValidateValue(value);

            if (Capabilities.Count >= MaxCapabilities)
            {
                throw new InvalidOperationException($"Cannot add more than {MaxCapabilities} capabilities.");
            }

            Capabilities.Add(key, value);
        }

        void ICollection<KeyValuePair<string, string>>.Add(KeyValuePair<string, string> item)
        {
            ValidateKey(item.Key);
            ValidateValue(item.Value);

            if (Capabilities.Count >= MaxCapabilities)
            {
                throw new InvalidOperationException($"Cannot add more than {MaxCapabilities} capabilities.");
            }

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

        private static void ValidateKey(string key)
        {
            if (key is null)
            {
                throw new ArgumentNullException(nameof(key));
            }

            if (key.Length == 0)
            {
                throw new ArgumentException("Capability key cannot be empty.", nameof(key));
            }

            if (key.Length > MaxKeyLength)
            {
                throw new ArgumentException($"Capability key cannot exceed {MaxKeyLength} characters.", nameof(key));
            }
        }

        private static void ValidateValue(string value)
        {
            if (value is null)
            {
                throw new ArgumentNullException(nameof(value));
            }

            if (value.Length > MaxValueLength)
            {
                throw new ArgumentException($"Capability value cannot exceed {MaxValueLength} characters.", nameof(value));
            }
        }
    }
}
