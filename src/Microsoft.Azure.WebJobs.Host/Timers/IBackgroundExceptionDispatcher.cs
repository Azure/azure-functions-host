// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Runtime.ExceptionServices;

namespace Microsoft.Azure.WebJobs.Host.Timers
{
    internal interface IBackgroundExceptionDispatcher
    {
        void Throw(ExceptionDispatchInfo exceptionInfo);
    }
}
