// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using Microsoft.Extensions.Configuration;

namespace Microsoft.Azure.WebJobs.Script
{
    /// <summary>
    /// Extension methods for <see cref="IConfiguration"/>.
    /// </summary>
    public static class ConfigurationExtensions
    {
        /// <summary>
        /// Gets a value indicating whether placeholder mode is enabled.
        /// </summary>
        /// <param name="configuration">The configuration.</param>
        /// <returns><see langword="true"/> when placeholder mode is enabled; otherwise, <see langword="false"/>.</returns>
        public static bool IsPlaceholderModeEnabled(this IConfiguration configuration)
        {
            ArgumentNullException.ThrowIfNull(configuration);

            return string.Equals(
                configuration[EnvironmentSettingNames.AzureWebsitePlaceholderMode],
                "1",
                StringComparison.Ordinal);
        }
    }
}
