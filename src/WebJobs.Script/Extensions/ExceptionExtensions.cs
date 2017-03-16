// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Reflection;
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
                if ((exception is OutOfMemoryException && !(exception is InsufficientMemoryException)) ||
                    exception is ThreadAbortException ||
                    exception is AccessViolationException ||
                    exception is SEHException ||
                    exception is StackOverflowException)
                {
                    return true;
                }

                if (exception is TypeInitializationException &&
                    exception is TargetInvocationException)
                {
                    break;
                }

                exception = exception.InnerException;
            }

            return false;
        }

        public static string ToFormattedString(this Exception exception)
        {
            if (exception == null)
            {
                throw new ArgumentNullException(nameof(exception));
            }

            return ExceptionFormatter.GetFormattedException(exception);
        }
    }
}
