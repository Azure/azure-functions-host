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
    /// of capabilities, and does not reflect all capabilities of the system.
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
        /// Attempts to set all capabilities from the provided collection.
        /// </summary>
        /// <param name="capabilities">An enumerable containing key value pairs with the capabilities to set.</param>
        /// <returns>
        /// <see langword="true"/> if the capabilities were applied; <see langword="false"/> if the
        /// provided capabilities were ignored.
        /// </returns>
        public bool TrySetAll(IEnumerable<KeyValuePair<string, string>> capabilities);

        /// <summary>
        /// Clears all capabilities from the store, removing all existing key-value pairs.
        /// </summary>
        public void Clear();
    }
}
