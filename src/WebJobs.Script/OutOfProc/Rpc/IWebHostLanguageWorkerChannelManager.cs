// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Collections.Generic;
using System.Threading.Tasks;

namespace Microsoft.Azure.WebJobs.Script.Rpc
{
    public interface IWebHostLanguageWorkerChannelManager
    {
        Task<ILanguageWorkerChannel> InitializeChannelAsync(string language);

        Dictionary<string, TaskCompletionSource<ILanguageWorkerChannel>> GetChannels(string language);

        Task SpecializeAsync();

        Task<bool> ShutdownChannelIfExistsAsync(string language, string workerId);

        Task ShutdownChannelsAsync();
    }
}
