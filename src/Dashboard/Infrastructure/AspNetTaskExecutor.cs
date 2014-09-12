// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;

namespace Dashboard.Infrastructure
{
    public static class AspNetTaskExecutor
    {
        private static readonly TaskFactory _factory = new TaskFactory(TaskScheduler.Default);

        public static void Execute(Func<Task> taskFactory)
        {
            if (taskFactory == null)
            {
                throw new ArgumentNullException("taskFactory");
            }

            CultureInfo capturedCulture = CultureInfo.CurrentCulture;
            CultureInfo capturedUICulture = CultureInfo.CurrentUICulture;

            _factory.StartNew(() =>
            {
                Thread.CurrentThread.CurrentCulture = capturedCulture;
                Thread.CurrentThread.CurrentUICulture = capturedUICulture;
                return taskFactory.Invoke();
            }).Unwrap().GetAwaiter().GetResult();
        }

        public static TResult Execute<TResult>(Func<Task<TResult>> taskFactory)
        {
            if (taskFactory == null)
            {
                throw new ArgumentNullException("taskFactory");
            }

            CultureInfo capturedCulture = CultureInfo.CurrentCulture;
            CultureInfo capturedUICulture = CultureInfo.CurrentUICulture;

            return _factory.StartNew(() =>
            {
                Thread.CurrentThread.CurrentCulture = capturedCulture;
                Thread.CurrentThread.CurrentUICulture = capturedUICulture;
                return taskFactory.Invoke();
            }).Unwrap().GetAwaiter().GetResult();
        }
    }
}
