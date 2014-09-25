// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Host.Executors;

namespace Microsoft.Azure.WebJobs.Host.FunctionalTests.TestDoubles
{
    internal class FakeHostIdProvider : IHostIdProvider
    {
        private readonly string _hostId = Guid.NewGuid().ToString("N");

        public Task<string> GetHostIdAsync(IEnumerable<MethodInfo> indexedMethods, CancellationToken cancellationToken)
        {
            return Task.FromResult(_hostId);
        }
    }
}
