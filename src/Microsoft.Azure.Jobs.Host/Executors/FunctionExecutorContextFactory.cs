// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using Microsoft.Azure.Jobs.Host.Bindings;
using Microsoft.Azure.Jobs.Host.Loggers;
using Microsoft.Azure.Jobs.Host.Protocols;

namespace Microsoft.Azure.Jobs.Host.Executors
{
    internal class FunctionExecutorFactory : IFunctionExecutorFactory
    {
        private readonly IFunctionInstanceLogger _functionInstanceLogger;
        private readonly IFunctionOutputLogger _functionOutputLogger;
        private readonly HostOutputMessage _hostOutputMessage;

        public FunctionExecutorFactory(IFunctionInstanceLogger functionInstanceLogger,
            IFunctionOutputLogger functionOutputLogger,
            HostOutputMessage hostOutputMessage)
        {
            _functionInstanceLogger = functionInstanceLogger;
            _functionOutputLogger = functionOutputLogger;
            _hostOutputMessage = hostOutputMessage;
        }

        public IFunctionExecutor Create(HostBindingContext context)
        {
            FunctionExecutorContext executorContext = new FunctionExecutorContext(_functionInstanceLogger,
                _functionOutputLogger, context, _hostOutputMessage);
            return new FunctionExecutor(executorContext);
        }
    }
}
