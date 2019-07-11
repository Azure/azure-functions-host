// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Threading.Channels;

namespace Microsoft.Azure.WebJobs.Script.WebHost.Diagnostics
{
    public interface IDeferredLogSource
    {
        ChannelReader<DeferredLogMessage> LogChannel { get; }
    }
}