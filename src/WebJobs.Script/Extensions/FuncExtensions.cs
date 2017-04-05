// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.Azure.WebJobs.Script
{
    public static class FuncExtensions
    {
        public static Func<T, Task> Debounce<T>(this Func<T, Task> func, int milliseconds = 300)
        {
            var last = 0;

            return async (arg) =>
            {
                var current = Interlocked.Increment(ref last);

                await Task.Delay(milliseconds);

                if (current == last)
                {
                    await func(arg);
                }
            };
        }

        public static Func<Task> Debounce(this Func<Task> targetAction, int milliseconds = 300)
        {
            Func<object, Task> action = _ => targetAction();
            action = action.Debounce(milliseconds);

            return () => action(null);
        }
    }
}
