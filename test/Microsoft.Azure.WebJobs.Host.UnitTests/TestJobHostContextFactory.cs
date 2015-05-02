// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Host.Bindings;
using Microsoft.Azure.WebJobs.Host.Executors;
using Microsoft.Azure.WebJobs.Host.Indexers;
using Microsoft.Azure.WebJobs.Host.Queues;
using Microsoft.Azure.WebJobs.Host.TestCommon;
using Microsoft.Azure.WebJobs.Host.Timers;

namespace Microsoft.Azure.WebJobs.Host.UnitTests
{
    internal class TestJobHostContextFactory : IJobHostContextFactory
    {
        public IStorageAccountProvider StorageAccountProvider { get; set; }

        public Task<JobHostContext> CreateAndLogHostStartedAsync(CancellationToken shutdownToken, CancellationToken cancellationToken)
        {
            ITypeLocator typeLocator = new DefaultTypeLocator(new StringWriter());
            INameResolver nameResolver = new RandomNameResolver();
            JobHostConfiguration config = new JobHostConfiguration
            {
                NameResolver = nameResolver,
                TypeLocator = typeLocator
            };

            return JobHostContextFactory.CreateAndLogHostStartedAsync(
                StorageAccountProvider, config.Queues, typeLocator, DefaultJobActivator.Instance, nameResolver,
                new NullConsoleProvider(), new JobHostConfiguration(), shutdownToken, cancellationToken,
                new FixedHostIdProvider(Guid.NewGuid().ToString("N")),
                null, new EmptyFunctionIndexProvider(),
                null, new NullHostInstanceLoggerProvider(), new NullFunctionInstanceLoggerProvider(), new NullFunctionOutputLoggerProvider());
        }

        public class NullFunctionExecutor : IFunctionExecutor
        {
            public Task<IDelayedException> TryExecuteAsync(IFunctionInstance instance, CancellationToken cancellationToken)
            {
                throw new NotImplementedException();
            }
        }
    }
}
