// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.Runtime.ExceptionServices;

namespace Microsoft.Azure.Jobs.Host.Executors
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
