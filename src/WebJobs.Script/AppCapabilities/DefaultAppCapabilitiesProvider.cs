// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Collections.Concurrent;
using System.Collections.Generic;

namespace Microsoft.Azure.WebJobs.Script.AppCapabilities
{
    internal class DefaultAppCapabilitiesProvider : IAppCapabilitiesProvider
    {
        private readonly ConcurrentDictionary<string, string> _capabilities = new ConcurrentDictionary<string, string>();

        public IReadOnlyDictionary<string, string> GetCapabilities()
        {
            return _capabilities;
        }

        public void SetCapability(string capability, string value)
        {
            _capabilities[capability] = value;
        }
    }
}
