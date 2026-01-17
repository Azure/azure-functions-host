// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Collections.Generic;

namespace Microsoft.Azure.WebJobs.Script.AppCapabilities
{
    internal class DefaultAppCapabilitiesProvider : IAppCapabilitiesProvider
    {
        private readonly Dictionary<string, string> _capabilities = new Dictionary<string, string>();

        public Dictionary<string, string> GetCapabilities()
        {
            return _capabilities;
        }

        public void SetCapability(string capability, string value)
        {
            _capabilities[capability] = value;
        }
    }
}
