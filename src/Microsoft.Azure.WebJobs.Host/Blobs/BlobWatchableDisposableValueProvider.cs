// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using Microsoft.Azure.WebJobs.Host.Bindings;
using Microsoft.Azure.WebJobs.Host.Storage.Blob;

namespace Microsoft.Azure.WebJobs.Host.Blobs
{
    internal sealed class BlobWatchableDisposableValueProvider : IValueProvider, IWatchable, IDisposable
    {
        private readonly IStorageBlob _blob;
        private readonly object _value;
        private readonly Type _valueType;
        private readonly IWatcher _watcher;
        private readonly IDisposable _disposable;

        private bool _disposed;

        public BlobWatchableDisposableValueProvider(IStorageBlob blob, object value, Type valueType, IWatcher watcher,
            IDisposable disposable)
        {
            if (value != null && !valueType.IsAssignableFrom(value.GetType()))
            {
                throw new InvalidOperationException("value is not of the correct type.");
            }

            _blob = blob;
            _value = value;
            _valueType = valueType;
            _watcher = watcher;
            _disposable = disposable;
        }

        public Type Type
        {
            get { return _valueType; }
        }

        public object GetValue()
        {
            return _value;
        }

        public string ToInvokeString()
        {
            return _blob.GetBlobPath();
        }

        public IWatcher Watcher
        {
            get { return _watcher; }
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                _disposable.Dispose();
                _disposed = true;
            }
        }
    }
}
