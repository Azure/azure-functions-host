// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Azure.Jobs.Host.Executors;
using Microsoft.Azure.Jobs.Host.Listeners;
using Microsoft.Azure.Jobs.Host.Triggers;
using Microsoft.WindowsAzure.Storage.Blob;

namespace Microsoft.Azure.Jobs.Host.Blobs.Listeners
{
    internal class BlobListenerFactory : IListenerFactory
    {
        private readonly CloudBlobContainer _container;
        private readonly IBlobPathSource _input;
        private readonly IEnumerable<IBindableBlobPath> _outputs;
        private readonly ITriggeredFunctionInstanceFactory<ICloudBlob> _instanceFactory;

        public BlobListenerFactory(CloudBlobContainer container, IBlobPathSource input,
            IEnumerable<IBindableBlobPath> outputs, ITriggeredFunctionInstanceFactory<ICloudBlob> instanceFactory)
        {
            _container = container;
            _input = input;
            _outputs = outputs;
            _instanceFactory = instanceFactory;
        }

        public Task<IListener> CreateAsync(IFunctionExecutor executor, ListenerFactoryContext context)
        {
            SharedBlobListener sharedListener = context.SharedListeners.GetOrCreate<SharedBlobListener>(
                new SharedBlobListenerFactory(context));
            BlobTriggerExecutor triggerExecutor = new BlobTriggerExecutor(_input, _outputs, _instanceFactory, executor);
            sharedListener.Register(_container, triggerExecutor);
            IListener listener = new BlobListener(sharedListener);
            return Task.FromResult(listener);
        }
    }
}
