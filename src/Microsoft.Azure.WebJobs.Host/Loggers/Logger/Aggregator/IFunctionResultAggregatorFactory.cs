// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using Microsoft.Extensions.Logging;

namespace Microsoft.Azure.WebJobs.Host.Loggers
{
    internal interface IFunctionResultAggregatorFactory
    {
        IAsyncCollector<FunctionInstanceLogEntry> Create(int batchSize, TimeSpan batchTimeout, ILoggerFactory loggerFactory);
    }
}
