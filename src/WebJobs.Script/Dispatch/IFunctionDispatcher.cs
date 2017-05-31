// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Script.Description;

namespace Microsoft.Azure.WebJobs.Script.Dispatch
{
    // maps from language type -> language worker pool
    internal interface IFunctionDispatcher
    {
        // read worker information from configuration
        // start workers?
        Task InitializeAsync(IEnumerable<LanguageWorkerConfig> workerConfigs);

        // assign functions to worker pools based on language type
        // load functions?
        Task LoadAsync(FunctionMetadata functionMetadata);

        // invoke a function
        // could use delay loading for start worker / load fucntion
        Task<object> InvokeAsync(FunctionMetadata functionMetadata, object[] parameters);

        Task ShutdownAsync();
    }
}
