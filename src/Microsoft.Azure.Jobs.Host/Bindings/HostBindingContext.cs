// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.Threading;
using Microsoft.Azure.Jobs.Host.Blobs;
using Microsoft.Azure.Jobs.Host.Executors;
using Microsoft.Azure.Jobs.Host.Queues;
using Microsoft.WindowsAzure.Storage;

namespace Microsoft.Azure.Jobs.Host.Bindings
{
    internal class HostBindingContext
    {
        private readonly IBindingProvider _bindingProvider;
        private readonly INameResolver _nameResolver;
        private readonly CloudStorageAccount _storageAccount;
        private readonly string _serviceBusConnectionString;

        public HostBindingContext(
            IBindingProvider bindingProvider,
            INameResolver nameResolver,
            CloudStorageAccount storageAccount,
            string serviceBusConnectionString)
        {
            _bindingProvider = bindingProvider;
            _nameResolver = nameResolver;
            _storageAccount = storageAccount;
            _serviceBusConnectionString = serviceBusConnectionString;
        }

        public IBindingProvider BindingProvider
        {
            get { return _bindingProvider; }
        }

        public INameResolver NameResolver
        {
            get { return _nameResolver; }
        }

        public CloudStorageAccount StorageAccount
        {
            get { return _storageAccount; }
        }

        public string ServiceBusConnectionString
        {
            get { return _serviceBusConnectionString; }
        }

        public IBlobWrittenWatcher BlobWrittenWatcher { get; set; }

        public IMessageEnqueuedWatcher MessageEnqueuedWatcher { get; set; }
    }
}
