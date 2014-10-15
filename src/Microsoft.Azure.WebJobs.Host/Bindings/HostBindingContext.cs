// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using Microsoft.Azure.WebJobs.Host.Blobs;
using Microsoft.Azure.WebJobs.Host.Queues;
using Microsoft.Azure.WebJobs.Host.Timers;

namespace Microsoft.Azure.WebJobs.Host.Bindings
{
    internal class HostBindingContext
    {
        private readonly IBackgroundExceptionDispatcher _backgroundExceptionDispatcher;
        private readonly IBindingProvider _bindingProvider;
        private readonly INameResolver _nameResolver;
        private readonly IQueueConfiguration _queueConfiguration;

        public HostBindingContext(
            IBackgroundExceptionDispatcher backgroundExceptionDispatcher,
            IBindingProvider bindingProvider,
            INameResolver nameResolver,
            IQueueConfiguration queueConfiguration)
        {
            _backgroundExceptionDispatcher = backgroundExceptionDispatcher;
            _bindingProvider = bindingProvider;
            _nameResolver = nameResolver;
            _queueConfiguration = queueConfiguration;
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

        public IBlobWrittenWatcher BlobWrittenWatcher { get; set; }

        public IMessageEnqueuedWatcher MessageEnqueuedWatcher { get; set; }
    }
}
