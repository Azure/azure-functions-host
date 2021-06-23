// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using Google.Protobuf.Collections;
using Microsoft.Azure.WebJobs.Script.Workers.Rpc;

namespace Microsoft.Azure.WebJobs.Script.Grpc
{
    public class WorkerCapabilities : IWorkerCapabilities
    {
        private IDictionary<string, IDictionary<string, string>> _workerCapabilities;

        public WorkerCapabilities()
        {
            _workerCapabilities = new ConcurrentDictionary<string, IDictionary<string, string>>();
        }

        public string GetCapabilityValue(string runtime, string capability)
        {
            string capabilityValue = string.Empty;
            IDictionary<string, string> runtimeCapabilities = new MapField<string, string>();
            _workerCapabilities.TryGetValue(runtime, out runtimeCapabilities);

            if (runtimeCapabilities != null)
            {
                runtimeCapabilities.TryGetValue(capability, out capabilityValue);
            }

            return capabilityValue;
        }

        public void UpdateCapabilities(string runtime, IDictionary<string, string> capabilities)
        {
            if (_workerCapabilities.ContainsKey(runtime))
            {
                _workerCapabilities[runtime] = capabilities;
            }
            else
            {
                _workerCapabilities.Add(runtime, capabilities);
            }
        }
    }
}