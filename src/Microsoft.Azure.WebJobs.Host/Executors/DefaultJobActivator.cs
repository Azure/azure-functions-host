// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;

namespace Microsoft.Azure.WebJobs.Host.Executors
{
    internal class DefaultJobActivator : IJobActivator
    {
        private static readonly DefaultJobActivator _instance = new DefaultJobActivator();

        private DefaultJobActivator()
        {
        }

        public static DefaultJobActivator Instance
        {
            get { return _instance; }
        }

        public T CreateInstance<T>()
        {
            return Activator.CreateInstance<T>();
        }
    }
}
