// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.Jobs.Host.Bindings;
using Microsoft.Azure.Jobs.Host.Listeners;
using Microsoft.Azure.Jobs.Host.Protocols;
using Microsoft.Azure.Jobs.Host.Timers;

namespace Microsoft.Azure.Jobs.Host.Executors
{
    internal class RunnerFactory : IRunnerFactory
    {
        private readonly ICanFailCommand _heartbeatCommand;
        private readonly HostBindingContextFactory _bindingContextFactory;
        private readonly IFunctionExecutorFactory _executorFactory;
        private readonly IListenerFactory _allFunctionsListenerFactory;
        private readonly IListenerFactory _abortOnlyListenerFactory;

        public RunnerFactory(ICanFailCommand heartbeatCommand, HostBindingContextFactory bindingContextFactory,
            IFunctionExecutorFactory executorFactory, IListenerFactory allFunctionsListenerFactory,
            IListenerFactory abortOnlyListenerFactory)
        {
            _heartbeatCommand = heartbeatCommand;
            _bindingContextFactory = bindingContextFactory;
            _executorFactory = executorFactory;
            _allFunctionsListenerFactory = allFunctionsListenerFactory;
            _abortOnlyListenerFactory = abortOnlyListenerFactory;
        }

        public Task<IRunner> CreateAndStartAsync(bool listenForAbortOnly, CancellationToken cancellationToken)
        {
            IListenerFactory listenerFactory =
                listenForAbortOnly ? _abortOnlyListenerFactory : _allFunctionsListenerFactory;
            return CreateAndStartAsync(listenerFactory, cancellationToken);
        }

        private async Task<IRunner> CreateAndStartAsync(IListenerFactory listenerFactory,
            CancellationToken cancellationToken)
        {
            IntervalSeparationTimer timer = CreateHeartbeatTimer(_heartbeatCommand);

            try
            {
                CancellationTokenSource hostCancellationTokenSource = new CancellationTokenSource();

                try
                {
                    WebJobsShutdownWatcher watcher = WebJobsShutdownWatcher.Create(hostCancellationTokenSource);

                    try
                    {
                        // The cancellation token here is the one used during the lifetime of the runner (to signal when
                        // the host is stopping; hostCancellationToken), not the one used during the host.StartAsync
                        // call (cancellationToken).
                        CancellationToken hostCancellationToken = hostCancellationTokenSource.Token;
                        HostBindingContext bindingContext = _bindingContextFactory.Create(hostCancellationToken);
                        IFunctionExecutor executor = _executorFactory.Create(bindingContext);
                        IListener listener = await CreateListenerAsync(listenerFactory, executor, bindingContext,
                            cancellationToken);

                        try
                        {
                            await _heartbeatCommand.TryExecuteAsync(cancellationToken);
                            timer.Start();
                            await listener.StartAsync(cancellationToken);

                            return new Runner(timer, hostCancellationTokenSource, watcher, executor,
                                listener);
                        }
                        catch
                        {
                            listener.Dispose();
                            throw;
                        }
                    }
                    catch
                    {
                        watcher.Dispose();
                        throw;
                    }
                }
                catch
                {
                    hostCancellationTokenSource.Dispose();
                    throw;
                }
            }
            catch
            {
                timer.Dispose();
                throw;
            }
        }

        private static IntervalSeparationTimer CreateHeartbeatTimer(ICanFailCommand heartbeatCommand)
        {
            return LinearSpeedupTimerCommand.CreateTimer(heartbeatCommand,
                HeartbeatIntervals.NormalSignalInterval, HeartbeatIntervals.MinimumSignalInterval);
        }

        private static Task<IListener> CreateListenerAsync(IListenerFactory listenerFactory, IFunctionExecutor executor,
            HostBindingContext context, CancellationToken cancellationToken)
        {
            ListenerFactoryContext listenerContext = new ListenerFactoryContext(context, new SharedListenerContainer(),
                cancellationToken);
            return listenerFactory.CreateAsync(executor, listenerContext);
        }
    }
}
