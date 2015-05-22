// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;

namespace Microsoft.Azure.WebJobs.Host.Executors
{
    internal class DefaultJobActivator : IJobActivator
    {
        private static readonly DefaultJobActivator Singleton = new DefaultJobActivator();

        private DefaultJobActivator()
        {
        }

        public static DefaultJobActivator Instance
        {
            get { return Singleton; }
        }

        public T CreateInstance<T>()
        {
            return Activator.CreateInstance<T>();
        }
    }
}
