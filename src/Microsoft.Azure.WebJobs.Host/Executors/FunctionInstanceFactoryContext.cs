// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Host.Protocols;

namespace Microsoft.Azure.WebJobs.Host.Executors
{
    internal class FunctionInstanceFactoryContext
    {
        public Guid Id { get; set; }
        public Guid? ParentId { get; set; }
        public ExecutionReason ExecutionReason { get; set; }
        public IDictionary<string, object> Parameters { get; set; }
        public Func<Func<Task>, Task> InvokeHandler { get; set; }
    }
}