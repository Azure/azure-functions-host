// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Runtime.ExceptionServices;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Azure.WebJobs.Host;
using Microsoft.Azure.WebJobs.Host.Timers;
using Microsoft.Azure.WebJobs.Script.Workers;
using Microsoft.Extensions.Logging;

namespace Microsoft.Azure.WebJobs.Script.WebHost
{
    public class WebScriptHostExceptionHandler : IWebJobsExceptionHandler
    {
        private readonly IApplicationLifetime _applicationLifetime;
        private readonly ILogger _logger;
        private readonly IFunctionInvocationDispatcherFactory _functionInvocationDispatcherFactory;

        public WebScriptHostExceptionHandler(IApplicationLifetime applicationLifetime, ILogger<WebScriptHostExceptionHandler> logger, IFunctionInvocationDispatcherFactory functionInvocationDispatcherFactory)
        {
            _applicationLifetime = applicationLifetime ?? throw new ArgumentNullException(nameof(applicationLifetime));
            _functionInvocationDispatcherFactory = functionInvocationDispatcherFactory;
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task OnTimeoutExceptionAsync(ExceptionDispatchInfo exceptionInfo, TimeSpan timeoutGracePeriod)
        {
            FunctionTimeoutException timeoutException = exceptionInfo.SourceException as FunctionTimeoutException;

            if (timeoutException?.Task != null)
            {
                // We may double the timeoutGracePeriod here by first waiting to see if the initial
                // function task that started the exception has completed.
                Task completedTask = await Task.WhenAny(timeoutException.Task, Task.Delay(timeoutGracePeriod));

                // If the function task has completed, simply return. The host has already logged the timeout.
                if (completedTask == timeoutException.Task)
                {
                    return;
                }
            }

            // We can't wait on this as it may cause a deadlock if the timeout was fired
            // by a Listener that cannot stop until it has completed.
            // TODO: DI (FACAVAL) The shutdown call will invoke the host stop... but we may need to do this
            // explicitly in order to pass the timeout.
            // Task ignoreTask = _hostManager.StopAsync();
            // Give the manager and all running tasks some time to shut down gracefully.
            //await Task.Delay(timeoutGracePeriod);
            IFunctionInvocationDispatcher functionInvocationDispatcher = _functionInvocationDispatcherFactory.GetFunctionDispatcher();
            if (!functionInvocationDispatcher.State.Equals(FunctionInvocationDispatcherState.Default))
            {
                _logger.LogWarning($"A function timeout has occurred. Restarting worker process executing invocationId '{timeoutException.InstanceId}'.", exceptionInfo.SourceException);
                // If invocation id is not found in any of the workers => worker is already disposed. No action needed.
                await functionInvocationDispatcher.RestartWorkerWithInvocationIdAsync(timeoutException.InstanceId.ToString());
                _logger.LogWarning("Restart of language worker process(es) completed.", exceptionInfo.SourceException);
            }
            else
            {
                LogErrorAndFlush("A function timeout has occurred. Host is shutting down.", exceptionInfo.SourceException);
                _applicationLifetime.StopApplication();
            }
        }

        public Task OnUnhandledExceptionAsync(ExceptionDispatchInfo exceptionInfo)
        {
            LogErrorAndFlush("An unhandled exception has occurred. Host is shutting down.", exceptionInfo.SourceException);

            _applicationLifetime.StopApplication();
            return Task.CompletedTask;
        }

        private void LogErrorAndFlush(string message, Exception exception)
        {
            _logger.LogError(0, exception, message);
        }
    }
}
