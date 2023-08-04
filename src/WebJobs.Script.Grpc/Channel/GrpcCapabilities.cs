// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace Microsoft.Azure.WebJobs.Script.Grpc
{
    internal class GrpcCapabilities
    {
        private readonly ILogger _logger;
        private IDictionary<string, string> _capabilities = new Dictionary<string, string>();

        public GrpcCapabilities(ILogger logger)
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

        public void UpdateCapabilities(IDictionary<string, string> capabilities, GrpcCapabilitiesUpdateStrategy strategy)
        {
            if (capabilities == null)
            {
                return;
            }

            _logger.LogDebug("Updating capabilities using {strategy} strategy. Current: {_capabilities} Incoming: {capabilities}", strategy, JsonSerializer.Serialize(_capabilities), JsonSerializer.Serialize(capabilities));

            switch (strategy)
            {
                case GrpcCapabilitiesUpdateStrategy.Merge:
                    foreach (KeyValuePair<string, string> capability in capabilities)
                    {
                        UpdateCapability(capability);
                    }
                    break;

                case GrpcCapabilitiesUpdateStrategy.Replace:
                    _capabilities.Clear();
                    foreach (KeyValuePair<string, string> capability in capabilities)
                    {
                        UpdateCapability(capability);
                    }
                    break;
                default:
                    throw new InvalidOperationException($"Did not recognize the capability update strategy {strategy}.");
            }

            _logger.LogDebug("Updated capabilities: {capabilities}", JsonSerializer.Serialize(_capabilities));
        }

        private void UpdateCapability(KeyValuePair<string, string> capability)
        {
            _capabilities[capability.Key] = capability.Value;
        }
    }
}