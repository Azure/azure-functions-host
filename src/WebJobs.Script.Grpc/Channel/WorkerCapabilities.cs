// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Collections.Generic;

namespace Microsoft.Azure.WebJobs.Script.Grpc
{
    public class WorkerCapabilities : IWorkerCapabilities
    {
        private IDictionary<string, IDictionary<string, string>> _workerCapabilities;

        public WorkerCapabilities()
        {
            _workerCapabilities = new Dictionary<string, IDictionary<string, string>>();
        }

        public IDictionary<string, string> GetCapabilities(string runtime)
        {
            IDictionary<string, string> capabilities = new Dictionary<string, string>();
            if (_workerCapabilities.TryGetValue(runtime, out capabilities))
            {
                return capabilities;
            }
            else
            {
                return null;
            }
        }

        public void SetCapabilities(string runtime, IDictionary<string, string> capabilities)
        {
            _workerCapabilities.Add(runtime, capabilities);
        }
    }
}