// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;

namespace Microsoft.Azure.WebJobs.Host.Executors
{
    internal class ActivatorInstanceFactory<TReflected> : IFactory<TReflected>
    {
        private readonly IJobActivator _activator;

        public ActivatorInstanceFactory(IJobActivator activator)
        {
            if (activator == null)
            {
                throw new ArgumentNullException("activator");
            }

            _activator = activator;
        }

        public TReflected Create()
        {
            return _activator.CreateInstance<TReflected>();
        }
    }
}
