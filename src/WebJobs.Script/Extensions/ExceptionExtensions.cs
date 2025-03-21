// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Collections.Generic;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using System.Threading;
using Microsoft.Azure.WebJobs.Host.Diagnostics;

namespace System
{
    internal static class ExceptionExtensions
    {
        public static bool IsFatal(this Exception exception)
        {
            while (exception != null)
            {
                if (exception
                    is (OutOfMemoryException and not InsufficientMemoryException)
                    or ThreadAbortException
                    or AccessViolationException
                    or SEHException
                    or StackOverflowException)
                {
                    return true;
                }

                exception = exception.InnerException;
            }

            return false;
        }

        public static string ToFormattedString(this Exception exception)
        {
            ArgumentNullException.ThrowIfNull(exception);
            return ExceptionFormatter.GetFormattedException(exception);
        }

        public static void ThrowIfErrorsPresent(IList<Exception> exceptions, string message = null)
        {
            switch (exceptions)
            {
                case null or []:
                    return;
                case [Exception e]:
                    ExceptionDispatchInfo.Capture(e).Throw();
                    return;
                default:
                    throw new AggregateException(message, exceptions);
            }
        }
    }
}
