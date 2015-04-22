// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

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
