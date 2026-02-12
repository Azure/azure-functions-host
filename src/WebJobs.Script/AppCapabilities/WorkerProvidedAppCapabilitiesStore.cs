// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;

namespace Microsoft.Azure.WebJobs.Script.AppCapabilities
{
    public sealed class WorkerProvidedAppCapabilitiesStore : IAppCapabilitiesStore
    {
        private readonly ConcurrentDictionary<string, string> _capabilities = new(StringComparer.OrdinalIgnoreCase);

        public IReadOnlyDictionary<string, string> Capabilities => _capabilities;

        public void Set(string key, string value) => _capabilities[key] = value;

        public void SetAll(IDictionary<string, string> capabilities)
        {
            foreach (var kvp in capabilities)
            {
                _capabilities[kvp.Key] = kvp.Value;
            }
        }
    }
}
