// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;

namespace Microsoft.Azure.Jobs.Host.Listeners
{
    internal interface ISharedListener<TFilter, TTriggerValue> : IDisposable
    {
        void Register(TFilter listenData, ITriggerExecutor<TTriggerValue> triggerExecutor);

        void EnsureAllStarted();

        void EnsureAllStopped();

        void EnsureAllDisposed();
    }
}
