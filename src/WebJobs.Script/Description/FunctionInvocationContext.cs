// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using Microsoft.Azure.WebJobs.Host;
using Microsoft.Extensions.Logging;

namespace Microsoft.Azure.WebJobs.Script.Description
{
    public class FunctionInvocationContext
    {
        public ExecutionContext ExecutionContext { get; set; }

        public Binder Binder { get; set; }

        public ILogger Logger { get; set; }
    }
}
