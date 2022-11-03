// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;

namespace Microsoft.Azure.WebJobs.Script.Config
{
    public interface IFunctionsHostingConfiguration
    {
        /// <summary>
        /// Occurs when hosting configuration was initialized from the config file.
        /// </summary>
        event EventHandler Initialized;

        /// <summary>
        /// Gets a value indicating whether the hosting configuration is initialized.
        /// </summary>
        bool IsInitialized { get; }

        /// <summary>
        /// Returns the value of an feature flag./>.
        /// </summary>
        /// <param name="name">The feature flag name.</param>
        /// <returns>The value of the feature flag specified by <paramref name="name"/>, or <see cref="null"/> if the feature flag is not found.</returns>
        string GetFeatureFlag(string name);

        /// <summary>
        /// Retrieves all feature flag names and their values from the hosting configuration.
        /// </summary>
        /// <returns>A dictionary that contains all feature flags and their values; otherwise, an empty dictionary if no feature flags are found.</returns>
        IDictionary<string, string> GetFeatureFlags();
    }
}
