// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.Azure.WebJobs.Script
{
    public static class ActionExtensions
    {
        public static Action<T> Debounce<T>(this Action<T> func, CancellationToken cancellationToken = default, int milliseconds = 300)
        {
            var last = 0;

            return (arg) =>
            {
                var current = Interlocked.Increment(ref last);

                Task.Delay(milliseconds).ContinueWith(t =>
                {
                    if (current == last)
                    {
                        // Only proceeed with the operation if there have been no
                        // more events within the specified time window (i.e. there
                        // is a quiet period)
                        if (!cancellationToken.IsCancellationRequested)
                        {
                            func(arg);
                        }
                    }
                    t.Dispose();
                });
            };
        }

        public static Action Debounce(this Action targetAction, CancellationToken cancellationToken = default, int milliseconds = 300)
        {
            Action<object> action = _ => targetAction();
            action = action.Debounce(cancellationToken, milliseconds);

            return () => action(null);
        }
    }
}
