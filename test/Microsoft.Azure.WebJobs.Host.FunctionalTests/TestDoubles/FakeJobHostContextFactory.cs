// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Host.Bindings;
using Microsoft.Azure.WebJobs.Host.Executors;
using Microsoft.Azure.WebJobs.Host.Indexers;
using Microsoft.Azure.WebJobs.Host.Loggers;
using Microsoft.Azure.WebJobs.Host.Queues;
using Microsoft.Azure.WebJobs.Host.TestCommon;
using Microsoft.Azure.WebJobs.Host.Timers;

namespace Microsoft.Azure.WebJobs.Host.FunctionalTests.TestDoubles
{
    internal class FakeJobHostContextFactory : IJobHostContextFactory
    {
        public ITypeLocator TypeLocator { get; set; }

        public IWebJobsExceptionHandler BackgroundExceptionDispatcher { get; set; }

        public IBindingProvider BindingProvider { get; set; }

        public IConsoleProvider ConsoleProvider { get; set; }

        public IFunctionIndexProvider FunctionIndexProvider { get; set; }

        public IFunctionInstanceLoggerProvider FunctionInstanceLoggerProvider { get; set; }

        public IFunctionOutputLoggerProvider FunctionOutputLoggerProvider { get; set; }

        public IHostIdProvider HostIdProvider { get; set; }

        public IHostInstanceLoggerProvider HostInstanceLoggerProvider { get; set; }

        public IQueueConfiguration QueueConfiguration { get; set; }

        public IStorageAccountProvider StorageAccountProvider { get; set; }

        public FunctionExecutor FunctionExecutor { get; set; }

        public Task<JobHostContext> CreateAndLogHostStartedAsync(JobHost host, CancellationToken shutdownToken, CancellationToken cancellationToken)
        {
            INameResolver nameResolver = new RandomNameResolver();
            JobHostConfiguration config = new JobHostConfiguration
            {
                NameResolver = nameResolver,
                TypeLocator = TypeLocator
            };

            return JobHostContextFactory.CreateAndLogHostStartedAsync(
                host, StorageAccountProvider, QueueConfiguration, TypeLocator, DefaultJobActivator.Instance, nameResolver,
                ConsoleProvider, new JobHostConfiguration(), shutdownToken, cancellationToken, BackgroundExceptionDispatcher, HostIdProvider, FunctionExecutor,
                FunctionIndexProvider, BindingProvider, HostInstanceLoggerProvider, FunctionInstanceLoggerProvider,
                FunctionOutputLoggerProvider);
        }
    }
}
