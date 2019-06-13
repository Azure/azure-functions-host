// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.Collections.Generic;

namespace Microsoft.Azure.WebJobs.Script.Rpc
{
    internal interface IJobHostLanguageWorkerChannelManager
    {
        void AddChannel(ILanguageWorkerChannel channel);

        void DisposeAndRemoveChannel(ILanguageWorkerChannel channel);

        void DisposeAndRemoveChannels();

        IEnumerable<ILanguageWorkerChannel> GetChannels();
    }
}
