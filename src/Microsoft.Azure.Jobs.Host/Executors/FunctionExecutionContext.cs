// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using Microsoft.Azure.Jobs.Host.Loggers;
using Microsoft.Azure.Jobs.Host.Protocols;

namespace Microsoft.Azure.Jobs.Host.Executors
{
    internal class FunctionExecutionContext
    {
        public HostOutputMessage HostOutputMessage { get; set; }

        public IFunctionOutputLogger OutputLogFactory { get; set; }

        public IFunctionInstanceLogger FunctionInstanceLogger { get; set; }
    }
}
