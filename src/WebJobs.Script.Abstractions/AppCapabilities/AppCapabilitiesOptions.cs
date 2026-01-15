// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

#nullable enable

using System;
using System.Collections.Generic;

namespace Microsoft.Azure.WebJobs.Script.AppCapabilities
{
    public sealed class AppCapabilitiesOptions
    {
        private readonly Dictionary<string, string> _capabilities;

        public AppCapabilitiesOptions()
        {
            _capabilities = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }

        public Dictionary<string, string> Capabilities => _capabilities;
    }
}