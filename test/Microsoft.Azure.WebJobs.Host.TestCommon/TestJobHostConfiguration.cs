// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using Microsoft.Azure.WebJobs.Host.Executors;

namespace Microsoft.Azure.WebJobs.Host.TestCommon
{
    internal class TestJobHostConfiguration : IServiceProvider
    {
        public IJobHostContextFactory ContextFactory { get; set; }

        public object GetService(Type serviceType)
        {
            if (serviceType == typeof(IJobHostContextFactory))
            {
                return ContextFactory;
            }
            else
            {
                return null;
            }
        }
    }
}
