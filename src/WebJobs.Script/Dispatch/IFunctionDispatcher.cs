// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Script.Description;

namespace Microsoft.Azure.WebJobs.Script.Dispatch
{
    // maps from language type -> language worker pool
    internal interface IFunctionDispatcher
    {
        // read worker information from configuration
        // start workers?
        Task Initialize();

        // assign functions to worker pools based on language type
        // load functions?
        Task Register(FunctionMetadata functionMetadata);

        // invoke a function
        // could use delay loading for start worker / load fucntion
        Task<object> Invoke(FunctionMetadata functionMetadata, object[] parameters);
    }
}
