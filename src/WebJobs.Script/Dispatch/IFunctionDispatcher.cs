// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using Microsoft.Azure.WebJobs.Script.Description;

namespace Microsoft.Azure.WebJobs.Script.Dispatch
{
    // maps from language type -> language worker pool
    internal interface IFunctionDispatcher : IDisposable
    {
        bool IsSupported(FunctionMetadata metadata);

        void Register(FunctionRegistrationContext context);
    }
}
