// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using Microsoft.Azure.WebJobs.Host.Bindings;
using Microsoft.Azure.WebJobs.Host.Loggers;
using Microsoft.Azure.WebJobs.Host.Protocols;

namespace Microsoft.Azure.WebJobs.Host.Executors
{
    internal class FunctionExecutorContext
    {
        private readonly IFunctionInstanceLogger _functionInstanceLogger;
        private readonly IFunctionOutputLogger _functionOutputLogger;
        private readonly HostBindingContext _bindingContext;
        private readonly HostOutputMessage _hostOutputMessage;

        public FunctionExecutorContext(IFunctionInstanceLogger functionInstanceLogger,
            IFunctionOutputLogger functionOutputLogger,
            HostBindingContext bindingContext,
            HostOutputMessage hostOutputMessage)
        {
            _functionInstanceLogger = functionInstanceLogger;
            _functionOutputLogger = functionOutputLogger;
            _bindingContext = bindingContext;
            _hostOutputMessage = hostOutputMessage;
        }

        public IFunctionInstanceLogger FunctionInstanceLogger
        {
            get { return _functionInstanceLogger; }
        }

        public IFunctionOutputLogger FunctionOutputLogger
        {
            get { return _functionOutputLogger; }
        }

        public HostBindingContext BindingContext
        {
            get { return _bindingContext; }
        }

        public HostOutputMessage HostOutputMessage
        {
            get { return _hostOutputMessage; }
        }
    }
}
