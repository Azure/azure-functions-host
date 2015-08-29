// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Host.Converters;
using Microsoft.Azure.WebJobs.Host.Storage.Blob;

namespace Microsoft.Azure.WebJobs.Host.Blobs
{
    internal class BlobContainerOutputConverter<TInput> : IAsyncObjectToTypeConverter<IStorageBlobContainer>
        where TInput : class
    {
        private readonly IAsyncConverter<TInput, IStorageBlobContainer> _innerConverter;

        public BlobContainerOutputConverter(IAsyncConverter<TInput, IStorageBlobContainer> innerConverter)
        {
            _innerConverter = innerConverter;
        }

        public async Task<ConversionResult<IStorageBlobContainer>> TryConvertAsync(object input,
            CancellationToken cancellationToken)
        {
            TInput typedInput = input as TInput;

            if (typedInput == null)
            {
                return new ConversionResult<IStorageBlobContainer>
                {
                    Succeeded = false,
                    Result = null
                };
            }

            IStorageBlobContainer container = await _innerConverter.ConvertAsync(typedInput, cancellationToken);

            return new ConversionResult<IStorageBlobContainer>
            {
                Succeeded = true,
                Result = container
            };
        }
    }
}
