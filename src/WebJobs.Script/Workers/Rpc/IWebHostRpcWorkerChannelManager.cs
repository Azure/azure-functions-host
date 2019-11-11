// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Collections.Generic;
using System.Threading.Tasks;

namespace Microsoft.Azure.WebJobs.Script.Workers.Rpc
{
    public interface IWebHostRpcWorkerChannelManager
    {
        IRpcWorkerChannel InitializeChannel(string language);

        Dictionary<string, IRpcWorkerChannel> GetChannels(string language);

        void Specialize();

        bool ShutdownChannelIfExists(string language, string workerId);

        void ShutdownChannels();
    }
}
