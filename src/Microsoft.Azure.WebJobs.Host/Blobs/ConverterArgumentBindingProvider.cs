// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Host.Bindings;
using Microsoft.Azure.WebJobs.Host.Converters;
using Microsoft.Azure.WebJobs.Host.Storage.Blob;

namespace Microsoft.Azure.WebJobs.Host.Blobs
{
    internal class ConverterArgumentBindingProvider<T> : IBlobArgumentBindingProvider
    {
        private readonly IConverter<IStorageBlob, T> _converter;

        public ConverterArgumentBindingProvider(IConverter<IStorageBlob, T> converter)
        {
            _converter = converter;
        }

        public IBlobArgumentBinding TryCreate(ParameterInfo parameter, FileAccess? access)
        {
            if (parameter.ParameterType != typeof(T))
            {
                return null;
            }

            if (access.HasValue && access.Value != ConverterArgumentBinding.DefaultAccess)
            {
                throw new InvalidOperationException("Cannot bind blob to " + typeof(T).Name + " using access "
                    + access.Value.ToString() + ".");
            }

            return new ConverterArgumentBinding(_converter);
        }

        private class ConverterArgumentBinding : IBlobArgumentBinding
        {
            internal const FileAccess DefaultAccess = FileAccess.ReadWrite;

            private readonly IConverter<IStorageBlob, T> _converter;

            public ConverterArgumentBinding(IConverter<IStorageBlob, T> converter)
            {
                _converter = converter;
            }

            public FileAccess Access
            {
                get { return DefaultAccess; }
            }

            public Type ValueType
            {
                get { return typeof(T); }
            }

            public Task<IValueProvider> BindAsync(IStorageBlob value, ValueBindingContext context)
            {
                IValueProvider provider = BlobValueProvider.Create(value, _converter.Convert(value));
                return Task.FromResult(provider);
            }
        }
    }
}
