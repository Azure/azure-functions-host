// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.Runtime.ExceptionServices;

namespace Microsoft.Azure.WebJobs.Host.Timers
{
    internal interface IBackgroundExceptionDispatcher
    {
        void Throw(ExceptionDispatchInfo exceptionInfo);
    }
}
