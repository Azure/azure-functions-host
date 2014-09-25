// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using Microsoft.Azure.WebJobs.Host.Blobs;
using Microsoft.Azure.WebJobs.Host.Queues;
using Microsoft.Azure.WebJobs.Host.Storage;
using Microsoft.Azure.WebJobs.Host.Timers;

namespace Microsoft.Azure.WebJobs.Host.Bindings
{
    internal class HostBindingContext
    {
        private readonly IBackgroundExceptionDispatcher _backgroundExceptionDispatcher;
        private readonly IBindingProvider _bindingProvider;
        private readonly INameResolver _nameResolver;
        private readonly IQueueConfiguration _queueConfiguration;
        private readonly IStorageAccount _storageAccount;
        private readonly string _serviceBusConnectionString;

        public HostBindingContext(
            IBackgroundExceptionDispatcher backgroundExceptionDispatcher,
            IBindingProvider bindingProvider,
            INameResolver nameResolver,
            IQueueConfiguration queueConfiguration,
            IStorageAccount storageAccount,
            string serviceBusConnectionString)
        {
            _backgroundExceptionDispatcher = backgroundExceptionDispatcher;
            _bindingProvider = bindingProvider;
            _nameResolver = nameResolver;
            _queueConfiguration = queueConfiguration;
            _storageAccount = storageAccount;
            _serviceBusConnectionString = serviceBusConnectionString;
        }

        public IBackgroundExceptionDispatcher BackgroundExceptionDispatcher
        {
            get { return _backgroundExceptionDispatcher; }
        }

        public IBindingProvider BindingProvider
        {
            get { return _bindingProvider; }
        }

        public INameResolver NameResolver
        {
            get { return _nameResolver; }
        }

        public IQueueConfiguration QueueConfiguration
        {
            get { return _queueConfiguration; }
        }

        public IStorageAccount StorageAccount
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
