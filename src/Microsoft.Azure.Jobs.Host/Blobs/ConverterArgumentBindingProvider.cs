// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using Microsoft.Azure.Jobs.Host.Bindings;
using Microsoft.Azure.Jobs.Host.Converters;
using Microsoft.WindowsAzure.Storage.Blob;

namespace Microsoft.Azure.Jobs.Host.Blobs
{
    internal class ConverterArgumentBindingProvider<T> : IBlobArgumentBindingProvider
    {
        private readonly IConverter<ICloudBlob, T> _converter;

        public ConverterArgumentBindingProvider(IConverter<ICloudBlob, T> converter)
        {
            _converter = converter;
        }

        public IBlobArgumentBinding TryCreate(ParameterInfo parameter, FileAccess? access)
        {
            if (parameter.ParameterType != typeof(T))
            {
                return null;
            }

            if (access.HasValue && access.Value != FileAccess.Read)
            {
                throw new InvalidOperationException("Cannot bind blob to " + typeof(T).Name + " using access "
                    + access.Value.ToString() + ".");
            }

            return new ConverterArgumentBinding(_converter);
        }

        private class ConverterArgumentBinding : IBlobArgumentBinding
        {
            private readonly IConverter<ICloudBlob, T> _converter;

            public ConverterArgumentBinding(IConverter<ICloudBlob, T> converter)
            {
                _converter = converter;
            }

            public FileAccess Access
            {
                get { return FileAccess.Read; }
            }

            public Type ValueType
            {
                get { return typeof(T); }
            }

            public Task<IValueProvider> BindAsync(ICloudBlob value, FunctionBindingContext context)
            {
                IValueProvider provider = new BlobValueProvider(value, _converter.Convert(value), typeof(T));
                return Task.FromResult(provider);
            }
        }
    }
}
