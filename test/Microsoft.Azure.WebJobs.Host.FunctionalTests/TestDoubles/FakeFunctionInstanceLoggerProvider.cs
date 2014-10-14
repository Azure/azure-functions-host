// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Host.Loggers;

namespace Microsoft.Azure.WebJobs.Host.FunctionalTests.TestDoubles
{
    internal class FakeFunctionInstanceLoggerProvider : IFunctionInstanceLoggerProvider
    {
        private readonly IFunctionInstanceLogger _logger;

        public FakeFunctionInstanceLoggerProvider(IFunctionInstanceLogger logger)
        {
            _logger = logger;
        }

        public Task<IFunctionInstanceLogger> GetAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult(_logger);
        }
    }
}
