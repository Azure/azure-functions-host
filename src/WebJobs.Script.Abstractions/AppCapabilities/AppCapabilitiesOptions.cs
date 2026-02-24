// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

#nullable enable

using System;
using System.Collections.Generic;

namespace Microsoft.Azure.WebJobs.Script.AppCapabilities
{
    public sealed class AppCapabilitiesOptions
    {
        /// <summary>
        ///  Gets the capabilities of the current instance, represented as a dictionary of key-value pairs.
        /// </summary>
        /// <remarks>The keys in the dictionary are case-insensitive, allowing for flexible access to
        /// capability values.</remarks>
        public IDictionary<string, string> Capabilities { get; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    }
}
