// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Runtime.ExceptionServices;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Host.Timers;

namespace Microsoft.Azure.WebJobs.Host.FunctionalTests.TestDoubles
{
    internal class TaskBackgroundExceptionHandler<TResult> : IWebJobsExceptionHandler
    {
        private readonly TaskCompletionSource<TResult> _taskSource;

        public TaskBackgroundExceptionHandler(TaskCompletionSource<TResult> taskSource)
        {
            _taskSource = taskSource;
        }

        public void Initialize(JobHost host)
        {
        }

        public Task OnTimeoutExceptionAsync(ExceptionDispatchInfo exceptionInfo, TimeSpan timeoutGracePeriod)
        {
            return Task.FromResult(0);
        }

        public Task OnUnhandledExceptionAsync(ExceptionDispatchInfo exceptionInfo)
        {
            Exception exception = exceptionInfo.SourceException;
            _taskSource.SetException(exception);
            return Task.FromResult(0);
        }
    }
}
