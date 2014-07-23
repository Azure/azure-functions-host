// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.Jobs.Host.Executors;
using Microsoft.WindowsAzure.Storage;

namespace Microsoft.Azure.Jobs.Host.TestCommon
{
    public class NullStorageCredentialsValidator : IStorageCredentialsValidator
    {
        public Task ValidateCredentialsAsync(CloudStorageAccount account, CancellationToken cancellationToken)
        {
            return Task.FromResult(0);
        }
    }
}
