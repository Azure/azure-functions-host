// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Threading;
using Microsoft.Azure.Jobs.Host.Bindings;
using Microsoft.Azure.Jobs.Host.Listeners;
using Microsoft.Azure.Jobs.Host.Protocols;
using Microsoft.Azure.Jobs.Host.Timers;

namespace Microsoft.Azure.Jobs.Host.Executors
{
    internal class RunnerFactory : IRunnerFactory
    {
        private readonly IHeartbeatCommand _heartbeatCommand;
        private readonly HostBindingContextFactory _bindingContextFactory;
        private readonly IFunctionExecutorFactory _executorFactory;
        private readonly IListenerFactory _allFunctionsListenerFactory;
        private readonly IListenerFactory _abortOnlyListenerFactory;

        public RunnerFactory(IHeartbeatCommand heartbeatCommand, HostBindingContextFactory bindingContextFactory,
            IFunctionExecutorFactory executorFactory, IListenerFactory allFunctionsListenerFactory,
            IListenerFactory abortOnlyListenerFactory)
        {
            _heartbeatCommand = heartbeatCommand;
            _bindingContextFactory = bindingContextFactory;
            _executorFactory = executorFactory;
            _allFunctionsListenerFactory = allFunctionsListenerFactory;
            _abortOnlyListenerFactory = abortOnlyListenerFactory;
        }

        public IRunner CreateAndStart(bool listenForAbortOnly, CancellationToken cancellationToken)
        {
            IListenerFactory listenerFactory =
                listenForAbortOnly ? _abortOnlyListenerFactory : _allFunctionsListenerFactory;
            return CreateAndStart(listenerFactory, cancellationToken);
        }

        private IRunner CreateAndStart(IListenerFactory listenerFactory, CancellationToken cancellationToken)
        {
            IntervalSeparationTimer timer = CreateHeartbeatTimer(_heartbeatCommand);

            try
            {
                WebJobsShutdownWatcher watcher = new WebJobsShutdownWatcher();

                try
                {
                    CancellationTokenSource combinedCancellationTokenSource =
                        CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, watcher.Token);

                    try
                    {
                        CancellationToken combinedCancellationToken = combinedCancellationTokenSource.Token;
                        HostBindingContext bindingContext = _bindingContextFactory.Create(cancellationToken);
                        IFunctionExecutor executor = _executorFactory.Create(bindingContext);
                        IListener listener = CreateListener(listenerFactory, executor, bindingContext);

                        try
                        {
                            timer.Start(executeFirst: true);
                            listener.Start();

                            IDisposable disposable = new CompositeDisposable(watcher, combinedCancellationTokenSource,
                                timer, listener);

                            return new Runner(disposable, timer, executor, listener, cancellationToken);
                        }
                        catch
                        {
                            listener.Dispose();
                            throw;
                        }
                    }
                    catch
                    {
                        combinedCancellationTokenSource.Dispose();
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
                timer.Dispose();
                throw;
            }
        }

        private static IntervalSeparationTimer CreateHeartbeatTimer(IHeartbeatCommand heartbeatCommand)
        {
            ICanFailCommand heartbeatTimerCommand = new UpdateHostHeartbeatCommand(heartbeatCommand);
            return LinearSpeedupTimerCommand.CreateTimer(heartbeatTimerCommand,
                HeartbeatIntervals.NormalSignalInterval, HeartbeatIntervals.MinimumSignalInterval);
        }

        private static IListener CreateListener(IListenerFactory listenerFactory, IFunctionExecutor executor,
            HostBindingContext context)
        {
            ListenerFactoryContext listenerContext = new ListenerFactoryContext(context, new SharedListenerContainer());
            return listenerFactory.Create(executor, listenerContext);
        }
    }
}
