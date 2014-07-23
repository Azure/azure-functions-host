// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.Jobs.Host.Executors;

namespace Microsoft.Azure.Jobs.Host.Loggers
{
    internal class ConsoleFunctionOutputLogger : IFunctionOutputLogger
    {
        public Task<IFunctionOutputDefinition> CreateAsync(IFunctionInstance instance,
            CancellationToken cancellationToken)
        {
            IFunctionOutputDefinition outputDefinition = new ConsoleFunctionOutputDefinition();
            return Task.FromResult(outputDefinition);
        }
    }
}
