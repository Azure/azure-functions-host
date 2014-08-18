// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.Azure.WebJobs.Host.Converters
{
    internal interface IAsyncObjectToTypeConverter<TOutput>
    {
        Task<ConversionResult<TOutput>> TryConvertAsync(object input, CancellationToken cancellationToken);
    }
}
