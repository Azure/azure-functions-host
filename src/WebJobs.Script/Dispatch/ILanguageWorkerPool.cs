// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Script.Description;

namespace Microsoft.Azure.WebJobs.Script.Dispatch
{
    // handles all functions & channels for a particular language type
    // maps from function -> channel
    // manages channel invocation data
    // - how to schedule new invocations if multiple channels
    // - how to recycle worker if unhealthy (timeout or error)
    internal interface ILanguageWorkerPool
    {
        // start workers
        Task Start();

        Task Load(FunctionMetadata functionMetadata);

        Task<object> Invoke(FunctionMetadata functionMetadata, object[] parameters);
    }
}
