// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Runtime.ExceptionServices;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Host.Timers;

namespace Microsoft.Azure.WebJobs.Host.TestCommon
{
    public class TestExceptionHandler : IWebJobsExceptionHandler
    {
        public void Initialize(JobHost host)
        {
        }

        public Task OnTimeoutExceptionAsync(ExceptionDispatchInfo exceptionInfo, TimeSpan timeoutGracePeriod)
        {
            Console.WriteLine("Timeout exception in test exception handler: {0}", exceptionInfo);

            return Task.CompletedTask;
        }

        public Task OnUnhandledExceptionAsync(ExceptionDispatchInfo exceptionInfo)
        {
            Console.WriteLine("Error in test exception handler: {0}", exceptionInfo);

            return Task.CompletedTask;
        }
    }
}
