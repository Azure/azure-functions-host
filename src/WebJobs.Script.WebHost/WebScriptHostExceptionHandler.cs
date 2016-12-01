// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Runtime.ExceptionServices;
using System.Threading.Tasks;
using System.Web.Hosting;
using Microsoft.Azure.WebJobs.Host;
using Microsoft.Azure.WebJobs.Host.Timers;

namespace Microsoft.Azure.WebJobs.Script.WebHost
{
    public class WebScriptHostExceptionHandler : IWebJobsExceptionHandler
    {
        private ScriptHostManager _manager;
        public WebScriptHostExceptionHandler(ScriptHostManager manager)
        {
            if (manager == null)
            {
                throw new ArgumentNullException(nameof(manager));
            }

            _manager = manager;
        }

        public void Initialize(JobHost host)
        {
        }

        public async Task OnTimeoutExceptionAsync(ExceptionDispatchInfo exceptionInfo, TimeSpan timeoutGracePeriod)
        {
            FunctionTimeoutException timeoutException = exceptionInfo.SourceException as FunctionTimeoutException;

            if (timeoutException?.Task != null)
            {
                // We may double the timeoutGracePeriod here by first waiting to see if the iniital
                // function task that started the exception has completed.
                Task completedTask = await Task.WhenAny(timeoutException.Task, Task.Delay(timeoutGracePeriod));

                // If the function task has completed, simply return. The host has already logged the timeout.
                if (completedTask == timeoutException.Task)
                {
                    return;
                }
            }

            LogErrorAndFlush("A function timeout has occurred. Host is shutting down.", exceptionInfo.SourceException);

            // We can't wait on this as it may cause a deadlock if the timeout was fired
            // by a Listener that cannot stop until it has completed.
            _manager.StopAsync().IgnoreFailure().Ignore();

            // Give the manager and all running tasks some time to shut down gracefully.
            await Task.Delay(timeoutGracePeriod);

            HostingEnvironment.InitiateShutdown();
        }

        public Task OnUnhandledExceptionAsync(ExceptionDispatchInfo exceptionInfo)
        {
            LogErrorAndFlush("An unhandled exception has occurred. Host is shutting down.", exceptionInfo.SourceException);
            HostingEnvironment.InitiateShutdown();
            return Task.CompletedTask;
        }

        private void LogErrorAndFlush(string message, Exception exception)
        {
            _manager.Instance.TraceWriter.Error(message, exception);
            _manager.Instance.TraceWriter.Flush();
        }
    }
}