// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.Azure.WebJobs.Host.Converters
{
    internal class AsyncConverter<TInput, TOutput> : IAsyncConverter<TInput, TOutput>
    {
        private readonly IConverter<TInput, TOutput> _innerConverter;

        public AsyncConverter(IConverter<TInput, TOutput> innerConverter)
        {
            _innerConverter = innerConverter;
        }

        public Task<TOutput> ConvertAsync(TInput input, CancellationToken cancellationToken)
        {
            TOutput result = _innerConverter.Convert(input);
            return Task.FromResult(result);
        }
    }
}
