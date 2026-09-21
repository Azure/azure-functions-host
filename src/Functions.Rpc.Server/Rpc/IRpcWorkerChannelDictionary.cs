// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Collections.Concurrent;

namespace Microsoft.Azure.WebJobs.Script.Workers.Rpc
{
    /// <summary>
    /// Class to hold list of <see cref="IRpcWorkerChannel"/>
    /// </summary>
    internal class IRpcWorkerChannelDictionary : ConcurrentDictionary<string, IRpcWorkerChannel>
    {
    }
}
