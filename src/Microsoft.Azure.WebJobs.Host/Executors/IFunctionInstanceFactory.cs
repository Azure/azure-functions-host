// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using Microsoft.Azure.WebJobs.Host.Protocols;

namespace Microsoft.Azure.WebJobs.Host.Executors
{
    internal interface IFunctionInstanceFactory
    {
        IFunctionInstance Create(Guid id, Guid? parentId, ExecutionReason reason, IDictionary<string, object> parameters);
    }
}
