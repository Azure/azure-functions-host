// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using Google.Protobuf.Collections;
using Microsoft.Extensions.Logging;

namespace Microsoft.Azure.WebJobs.Script.Rpc
{
    internal class Capabilities
    {
        private readonly ILogger _logger;
        private IDictionary<string, string> _capabilities = new Dictionary<string, string>();

        public Capabilities(ILogger logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

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
            if (capabilities == null)
            {
                return;
            }

            _logger.LogDebug($"Updating capabilities: {capabilities.ToString()}");

            foreach (KeyValuePair<string, string> capability in capabilities)
            {
                UpdateCapability(capability);
            }
        }

        private void UpdateCapability(KeyValuePair<string, string> capability)
        {
            _capabilities[capability.Key] = capability.Value;
        }
    }
}
