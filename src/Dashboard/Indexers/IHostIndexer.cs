// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using Microsoft.Azure.WebJobs.Protocols;

namespace Dashboard.Indexers
{
    public interface IHostIndexer
    {
        void ProcessHostStarted(HostStartedMessage message);
    }
}
