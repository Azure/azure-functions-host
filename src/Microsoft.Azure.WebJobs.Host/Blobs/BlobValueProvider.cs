// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using Microsoft.Azure.WebJobs.Host.Bindings;
using Microsoft.Azure.WebJobs.Host.Storage.Blob;

namespace Microsoft.Azure.WebJobs.Host.Blobs
{
    internal sealed class BlobValueProvider : IValueProvider
    {
        private readonly IStorageBlob _blob;
        private readonly object _value;
        private readonly Type _valueType;

        public BlobValueProvider(IStorageBlob blob, object value, Type valueType)
        {
            if (value != null && !valueType.IsAssignableFrom(value.GetType()))
            {
                throw new InvalidOperationException("value is not of the correct type.");
            }

            _blob = blob;
            _value = value;
            _valueType = valueType;
        }

        public static BlobValueProvider Create<T>(IStorageBlob blob, T value)
        {
            return new BlobValueProvider(blob, value: value, valueType: typeof(T));
        }

        public static BlobValueProvider CreateWithNull<T>(IStorageBlob blob) where T : class
        {
            return new BlobValueProvider(blob, value: null, valueType: typeof(T));
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
    }
}
