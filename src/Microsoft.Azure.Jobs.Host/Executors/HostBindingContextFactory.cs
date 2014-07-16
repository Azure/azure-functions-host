// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.Threading;
using Microsoft.Azure.Jobs.Host.Bindings;
using Microsoft.WindowsAzure.Storage;

namespace Microsoft.Azure.Jobs.Host.Executors
{
    internal class HostBindingContextFactory
    {
        private readonly IBindingProvider _bindingProvider;
        private readonly INameResolver _nameResolver;
        private readonly CloudStorageAccount _storageAccount;
        private readonly string _serviceBusConnectionString;

        public HostBindingContextFactory(IBindingProvider bindingProvider, INameResolver nameResolver,
            CloudStorageAccount storageAccount, string serviceBusConnectionString)
        {
            _bindingProvider = bindingProvider;
            _nameResolver = nameResolver;
            _storageAccount = storageAccount;
            _serviceBusConnectionString = serviceBusConnectionString;
        }

        public HostBindingContext Create(CancellationToken cancellationToken)
        {
            return new HostBindingContext(_bindingProvider, cancellationToken, _nameResolver, _storageAccount,
                _serviceBusConnectionString);
        }
    }
}
