// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;

namespace Microsoft.Azure.Jobs.Host.Executors
{
    internal class DelayedException : IDelayedException
    {
        private readonly Exception _exception;

        public DelayedException(Exception exception)
        {
            _exception = exception;
        }

        public void Throw()
        {
            throw _exception;
        }
    }
}
