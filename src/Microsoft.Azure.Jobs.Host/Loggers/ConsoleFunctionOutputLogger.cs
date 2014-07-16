// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using Microsoft.Azure.Jobs.Host.Executors;

namespace Microsoft.Azure.Jobs.Host.Loggers
{
    internal class ConsoleFunctionOuputLogFactory : IFunctionOutputLogger
    {
        public IFunctionOutputDefinition Create(IFunctionInstance instance)
        {
            return new ConsoleFunctionOutputDefinition();
        }
    }
}
