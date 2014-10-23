// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.Threading;

namespace Microsoft.Azure.WebJobs.Host.Listeners
{
    internal class ListenerFactoryContext
    {
        private readonly SharedListenerContainer _sharedListeners;
        private readonly CancellationToken _cancellationToken;

        public ListenerFactoryContext(SharedListenerContainer sharedListeners,
            CancellationToken cancellationToken)
        {
            _sharedListeners = sharedListeners;
            _cancellationToken = cancellationToken;
        }

        public SharedListenerContainer SharedListeners
        {
            get { return _sharedListeners; }
        }

        public CancellationToken CancellationToken
        {
            get { return _cancellationToken; }
        }
    }
}
