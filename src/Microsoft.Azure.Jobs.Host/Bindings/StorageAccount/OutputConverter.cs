// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using Microsoft.Azure.Jobs.Host.Converters;
using Microsoft.WindowsAzure.Storage;

namespace Microsoft.Azure.Jobs.Host.Bindings.StorageAccount
{
    internal class OutputConverter<TInput> : IObjectToTypeConverter<CloudStorageAccount>
        where TInput : class
    {
        private readonly IConverter<TInput, CloudStorageAccount> _innerConverter;

        public OutputConverter(IConverter<TInput, CloudStorageAccount> innerConverter)
        {
            _innerConverter = innerConverter;
        }

        public bool TryConvert(object input, out CloudStorageAccount output)
        {
            TInput typedInput = input as TInput;

            if (typedInput == null)
            {
                output = null;
                return false;
            }

            output = _innerConverter.Convert(typedInput);
            return true;
        }
    }
}
