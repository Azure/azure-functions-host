// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Threading.Tasks;

namespace Microsoft.Azure.WebJobs.Script
{
    /// <summary>Provides extension methods for the <see cref="IBinder"/> interface.</summary>
    public static class BinderExtensions
    {
        public static Task<object> BindAsync(this IBinder binder, Type targetBindingType, Attribute attribute)
        {
            // Would be nice to expose a non-generic overload of BindAsync that takes a type to avoid this...
            var methodInfo = typeof(IBinder).GetMethod("BindAsync").MakeGenericMethod(targetBindingType);

            Task invocationTask = (Task)methodInfo.Invoke(binder, new object[] { attribute, System.Threading.CancellationToken.None });

            return invocationTask.ContinueWith(t => t.GetType().GetProperty("Result").GetValue(invocationTask));
        }
    }
}
