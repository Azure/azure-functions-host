// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.Threading;
using System.Threading.Tasks;
using Microsoft.WindowsAzure.Storage;

namespace Microsoft.Azure.Jobs.Host.Executors
{
    internal class JobHostContextFactory
    {
        private readonly CloudStorageAccount _dashboardAccount;
        private readonly CloudStorageAccount _storageAccount;
        private readonly string _serviceBusConnectionString;
        private readonly IStorageCredentialsValidator _credentialsValidator;
        private readonly ITypeLocator _typeLocator;
        private readonly INameResolver _nameResolver;

        public JobHostContextFactory(CloudStorageAccount dashboardAccount,
            CloudStorageAccount storageAccount,
            string serviceBusConnectionString,
            IStorageCredentialsValidator credentialsValidator,
            ITypeLocator typeLocator,
            INameResolver nameResolver)
        {
            _dashboardAccount = dashboardAccount;
            _storageAccount = storageAccount;
            _serviceBusConnectionString = serviceBusConnectionString;
            _credentialsValidator = credentialsValidator;
            _typeLocator = typeLocator;
            _nameResolver = nameResolver;
        }

        public Task<JobHostContext> CreateAndLogHostStartedAsync(CancellationToken cancellationToken)
        {
            return JobHostContext.CreateAndLogHostStartedAsync(_dashboardAccount, _storageAccount,
                _serviceBusConnectionString, _credentialsValidator, _typeLocator, _nameResolver, cancellationToken);
        }
    }
}
