// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Runtime.ExceptionServices;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Host.Timers;

namespace Microsoft.Azure.WebJobs.Host.FunctionalTests.TestDoubles
{
    internal class TaskBackgroundExceptionDispatcher<TResult> : IBackgroundExceptionDispatcher
    {
        private readonly TaskCompletionSource<TResult> _taskSource;

        public TaskBackgroundExceptionDispatcher(TaskCompletionSource<TResult> taskSource)
        {
            _taskSource = taskSource;
        }

        public void Throw(ExceptionDispatchInfo exceptionInfo)
        {
            Exception exception = exceptionInfo.SourceException;
            _taskSource.SetException(exception);
        }
    }
}
