// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Host.Bindings;
using Microsoft.Azure.WebJobs.Host.Executors;
using Microsoft.Azure.WebJobs.Host.Indexers;
using Microsoft.Azure.WebJobs.Host.Queues;
using Microsoft.Azure.WebJobs.Host.Timers;

namespace Microsoft.Azure.WebJobs.Host.TestCommon
{
    internal class TestJobHostContextFactory : IJobHostContextFactory
    {
        public IFunctionIndexProvider FunctionIndexProvider { get; set; }

        public IQueueConfiguration Queues { get; set; }

        public IStorageAccountProvider StorageAccountProvider { get; set; }

        public Task<JobHostContext> CreateAndLogHostStartedAsync(CancellationToken shutdownToken,
            CancellationToken cancellationToken)
        {
            IBindingProvider bindingProvider = null;
            return JobHostContextFactory.CreateAndLogHostStartedAsync(StorageAccountProvider, FunctionIndexProvider,
                bindingProvider, new FixedHostIdProvider(Guid.NewGuid().ToString("N")),
                new NullHostInstanceLoggerProvider(), new NullFunctionInstanceLoggerProvider(),
                new NullFunctionOutputLoggerProvider(), Queues, BackgroundExceptionDispatcher.Instance,
                new NullConsoleProvider(), shutdownToken, cancellationToken);
        }
    }
}
