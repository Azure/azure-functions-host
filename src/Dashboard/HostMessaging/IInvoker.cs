// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using Dashboard.Data;
using Microsoft.Azure.Jobs.Protocols;

namespace Dashboard.HostMessaging
{
    public interface IInvoker
    {
        Guid TriggerAndOverride(string queueName, FunctionSnapshot function, IDictionary<string, string> arguments,
            Guid? parentId, ExecutionReason reason);
    }
}
