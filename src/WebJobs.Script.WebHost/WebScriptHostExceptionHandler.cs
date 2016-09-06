// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Runtime.ExceptionServices;
using System.Threading.Tasks;
using System.Web.Hosting;
using Microsoft.Azure.WebJobs.Host.Timers;

namespace Microsoft.Azure.WebJobs.Script.WebHost
{
    public class WebScriptHostExceptionHandler : IWebJobsExceptionHandler
    {
        private WebScriptHostManager _manager;
        public WebScriptHostExceptionHandler(WebScriptHostManager manager)
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

        public Task OnTimeoutExceptionAsync(ExceptionDispatchInfo exceptionInfo, TimeSpan timeoutGracePeriod)
        {
            LogErrorAndFlush("A function timeout has occurred. Host is shutting down.", exceptionInfo.SourceException);

            // We can't wait on this as it may cause a deadlock if the timeout was fired
            // by a Listener that cannot stop until it has completed.
            Task taskIgnore = _manager.StopAsync();

            // Give the manager some time to shut down gracefully.
            Task.Delay(timeoutGracePeriod).ContinueWith((ct) =>
            {
                HostingEnvironment.InitiateShutdown();
            });

            return Task.CompletedTask;
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