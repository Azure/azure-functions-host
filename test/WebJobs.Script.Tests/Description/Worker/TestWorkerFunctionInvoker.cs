// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Collections.ObjectModel;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Azure.WebJobs.Script.Abstractions.Description;
using Microsoft.Azure.WebJobs.Script.Binding;
using Microsoft.Azure.WebJobs.Script.Description;
using Microsoft.Azure.WebJobs.Script.Workers;
using Microsoft.Extensions.Logging;

namespace Microsoft.Azure.WebJobs.Script.Tests
{
    internal class TestWorkerFunctionInvoker : WorkerFunctionInvoker
    {
        public TestWorkerFunctionInvoker(ScriptHost host, BindingMetadata bindingMetadata, FunctionMetadata functionMetadata, ILoggerFactory loggerFactory,
        Collection<FunctionBinding> inputBindings, Collection<FunctionBinding> outputBindings, IFunctionInvocationDispatcher functionDispatcher, IApplicationLifetime applicationLifetime)
        : base(host, bindingMetadata, functionMetadata, loggerFactory, inputBindings, outputBindings, functionDispatcher, applicationLifetime)
        {
        }

        public new Task<object> InvokeCore(object[] parameters, FunctionInvocationContext context)
        {
            return base.InvokeCore(parameters, context);
        }
    }
}