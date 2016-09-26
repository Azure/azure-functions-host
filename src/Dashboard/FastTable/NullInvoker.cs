// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using Dashboard.HostMessaging;
using Microsoft.Azure.WebJobs.Protocols;

namespace Dashboard.Data
{
    internal class NullInvoker : IInvoker
    {
        public Guid TriggerAndOverride(string queueName, FunctionSnapshot function, IDictionary<string, string> arguments, Guid? parentId, ExecutionReason reason)
        {
            throw new NotImplementedException();
        }
    }
}