// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Host.Converters;
using Microsoft.Azure.WebJobs.Host.Storage.Blob;

namespace Microsoft.Azure.WebJobs.Host.Blobs
{
    internal class BlobOutputConverter<TInput> : IAsyncObjectToTypeConverter<IStorageBlob>
        where TInput : class
    {
        private readonly IAsyncConverter<TInput, IStorageBlob> _innerConverter;

        public BlobOutputConverter(IAsyncConverter<TInput, IStorageBlob> innerConverter)
        {
            _innerConverter = innerConverter;
        }

        public async Task<ConversionResult<IStorageBlob>> TryConvertAsync(object input,
            CancellationToken cancellationToken)
        {
            TInput typedInput = input as TInput;

            if (typedInput == null)
            {
                return new ConversionResult<IStorageBlob>
                {
                    Succeeded = false,
                    Result = null
                };
            }

            IStorageBlob blob = await _innerConverter.ConvertAsync(typedInput, cancellationToken);

            return new ConversionResult<IStorageBlob>
            {
                Succeeded = true,
                Result = blob
            };
        }
    }
}
