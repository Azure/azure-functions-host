// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;

namespace Microsoft.Azure.Jobs
{
    /// <summary>Provides extension methods for the <see cref="IBinder"/> interface.</summary>
    public static class BinderExtensions
    {
        /// <summary>Binds the specified attribute.</summary>
        /// <typeparam name="T">The type to which to bind.</typeparam>
        /// <param name="binder">The binder to use to bind.</param>
        /// <param name="attribute">The attribute to bind.</param>
        /// <returns>The value bound.</returns>
        public static T Bind<T>(this IBinder binder, Attribute attribute)
        {
            if (binder == null)
            {
                throw new ArgumentNullException("binder");
            }

            return binder.BindAsync<T>(attribute).Result;
        }
    }
}
