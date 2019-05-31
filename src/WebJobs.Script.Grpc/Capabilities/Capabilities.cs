// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using Google.Protobuf.Collections;

namespace Microsoft.Azure.WebJobs.Script.Grpc.Capabilities
{
    public class Capabilities
    {
        private static IDictionary<string, string> _capabilities = new Dictionary<string, string>();

        public static bool IsCapabilityEnabled(string capability)
        {
            string isEnabled = "false";
            bool enabled = false;
            _capabilities.TryGetValue(capability, out isEnabled);
            bool.TryParse(isEnabled, out enabled);
            return enabled;
        }

        public static string GetCapabilityState(string capability)
        {
            string state = string.Empty;
            _capabilities.TryGetValue(capability, out state);
            return state;
        }

        public static void UpdateCapabilities(MapField<string, string> capabilities)
        {
            foreach (KeyValuePair<string, string> capability in capabilities)
            {
                UpdateCapability(capability);
            }
        }

        public static void UpdateCapability(KeyValuePair<string, string> capability)
        {
            bool enable = false;
            if (bool.TryParse(capability.Value, out enable))
            {
                _capabilities[capability.Key] = enable.ToString();
            }
            else
            {
                // capability has one of a range of values
                _capabilities[capability.Key] = capability.Value;
            }
        }
    }
}
