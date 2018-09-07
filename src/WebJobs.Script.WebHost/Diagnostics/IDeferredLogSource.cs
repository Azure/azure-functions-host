// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Threading.Tasks.Dataflow;

namespace Microsoft.Azure.WebJobs.Script.WebHost.Diagnostics
{
    internal interface IDeferredLogSource
    {
        ISourceBlock<DeferredLogMessage> LogBuffer { get; }
    }
}