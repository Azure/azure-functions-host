// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using Google.Protobuf.Collections;

namespace Microsoft.Azure.WebJobs.Script.Grpc.Capabilities
{
    internal class Capabilities
    {
        private IDictionary<string, string> _capabilities = new Dictionary<string, string>();

        public string GetCapabilityState(string capability)
        {
            if (_capabilities.TryGetValue(capability, out string state))
            {
                return state;
            }
            return null;
        }

        public void UpdateCapabilities(MapField<string, string> capabilities)
        {
            foreach (KeyValuePair<string, string> capability in capabilities)
            {
                UpdateCapability(capability);
            }
        }

        public void UpdateCapability(KeyValuePair<string, string> capability)
        {
            _capabilities[capability.Key] = capability.Value;
        }
    }
}
