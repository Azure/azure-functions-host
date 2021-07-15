// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Collections.Generic;

namespace Microsoft.Azure.WebJobs.Script.Grpc
{
    public interface IWorkerCapabilities
    {
        string GetCapabilityValue(string runtime, string capability);

        void UpdateCapabilities(string runtime, IDictionary<string, string> capabilities);
    }
}