// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Diagnostics;
using System.Runtime.ExceptionServices;
using System.Threading;

namespace Microsoft.Azure.WebJobs.Host.Timers
{
    internal class BackgroundExceptionDispatcher : IBackgroundExceptionDispatcher
    {
        private static readonly BackgroundExceptionDispatcher _instance = new BackgroundExceptionDispatcher();

        private BackgroundExceptionDispatcher()
        {
        }

        public static BackgroundExceptionDispatcher Instance
        {
            get { return _instance; }
        }

        public void Throw(ExceptionDispatchInfo exceptionInfo)
        {
            Debug.Assert(exceptionInfo != null);

            Thread thread = new Thread(() =>
            {
                exceptionInfo.Throw();
            });
            thread.Start();
            thread.Join();
        }
    }
}
