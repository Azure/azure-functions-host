// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Collections.Generic;

namespace Microsoft.Azure.WebJobs.Script.AppCapabilities
{
    /// <summary>
    /// Provides a store for managing application capabilities as key-value pairs.
    /// </summary>
    /// <remarks>
    /// Application capabilities represent features and characteristics of the current
    /// application instance that can be queried and updated at runtime. This is just one source
    /// of capabilities, and does not reflect all capabailities of the system.
    /// </remarks>
    public interface IAppCapabilitiesStore
    {
        /// <summary>
        /// Gets the capabilities of the current instance, represented as a dictionary of key-value pairs.
        /// </summary>
        /// <value>
        /// A read-only dictionary containing the current application capabilities,
        /// where the key represents the capability name and the value represents the capability value.
        /// </value>
        public IReadOnlyDictionary<string, string> Capabilities { get; }

        /// <summary>
        /// Sets a single capability with the specified key and value.
        /// </summary>
        /// <param name="key">The capability key to set.</param>
        /// <param name="value">The capability value to associate with the key.</param>
        /// <remarks>
        /// If the capability already exists, its value will be updated. Otherwise, a new capability will be added.
        /// </remarks>
        public void Set(string key, string value);

        /// <summary>
        /// Sets multiple capabilities from the provided dictionary.
        /// </summary>
        /// <param name="capabilities">A dictionary containing the capabilities to set.</param>
        /// <remarks>
        /// This method updates existing capabilities and adds new ones from the provided dictionary.
        /// Existing capabilities not included in the dictionary remain unchanged.
        /// </remarks>
        public void SetAll(IDictionary<string, string> capabilities);
    }
}
