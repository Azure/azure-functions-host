// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Host.Executors;
using Microsoft.Azure.WebJobs.Host.Storage;

namespace Microsoft.Azure.WebJobs.Host.FunctionalTests
{
    internal class FakeStorageCredentialsValidator : IStorageCredentialsValidator
    {
        public Task ValidateCredentialsAsync(IStorageAccount account, CancellationToken cancellationToken)
        {
            return Task.FromResult(0);
        }
    }
}
