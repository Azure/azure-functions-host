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
using Microsoft.Azure.WebJobs.Host.Timers;

namespace Microsoft.Azure.WebJobs.Host.TestCommon
{
    internal class TestJobHostContextFactory : IJobHostContextFactory
    {
        public IFunctionIndexProvider FunctionIndexProvider { get; set; }

        public IQueueConfiguration Queues { get; set; }

        public IStorageAccountProvider StorageAccountProvider { get; set; }

        public SingletonManager SingletonManager { get; set; }

        public Task<JobHostContext> CreateAndLogHostStartedAsync(CancellationToken shutdownToken, CancellationToken cancellationToken)
        {
            ITypeLocator typeLocator = new DefaultTypeLocator(new StringWriter(), new DefaultExtensionRegistry());
            INameResolver nameResolver = new RandomNameResolver();
            JobHostConfiguration config = new JobHostConfiguration
            {
                NameResolver = nameResolver,
                TypeLocator = typeLocator
            };

            return JobHostContextFactory.CreateAndLogHostStartedAsync(
                StorageAccountProvider, Queues, typeLocator, DefaultJobActivator.Instance, nameResolver,
                new NullConsoleProvider(), new JobHostConfiguration(), shutdownToken, cancellationToken,
                functionIndexProvider: FunctionIndexProvider, singletonManager: SingletonManager);
        }
    }
}
