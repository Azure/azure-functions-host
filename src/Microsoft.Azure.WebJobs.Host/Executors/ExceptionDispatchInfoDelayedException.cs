// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Runtime.ExceptionServices;

namespace Microsoft.Azure.WebJobs.Host.Executors
{
    internal class ExceptionDispatchInfoDelayedException : IDelayedException
    {
        private readonly ExceptionDispatchInfo _exceptionInfo;

        public ExceptionDispatchInfoDelayedException(ExceptionDispatchInfo exceptionInfo)
        {
            _exceptionInfo = exceptionInfo;
        }

        public void Throw()
        {
            _exceptionInfo.Throw();
        }
    }
}
