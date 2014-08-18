// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Host.Converters;
using Microsoft.WindowsAzure.Storage.Blob;

namespace Microsoft.Azure.WebJobs.Host.Blobs
{
    internal class OutputConverter<TInput> : IAsyncObjectToTypeConverter<ICloudBlob>
        where TInput : class
    {
        private readonly IAsyncConverter<TInput, ICloudBlob> _innerConverter;

        public OutputConverter(IAsyncConverter<TInput, ICloudBlob> innerConverter)
        {
            _innerConverter = innerConverter;
        }

        public async Task<ConversionResult<ICloudBlob>> TryConvertAsync(object input,
            CancellationToken cancellationToken)
        {
            TInput typedInput = input as TInput;

            if (typedInput == null)
            {
                return new ConversionResult<ICloudBlob>
                {
                    Succeeded = false,
                    Result = null
                };
            }

            ICloudBlob blob = await _innerConverter.ConvertAsync(typedInput, cancellationToken);

            return new ConversionResult<ICloudBlob>
            {
                Succeeded = true,
                Result = blob
            };
        }
    }
}
