// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using Microsoft.Azure.WebJobs.Host.Converters;
using Microsoft.Azure.WebJobs.Host.Storage.Table;

namespace Microsoft.Azure.WebJobs.Host.Tables
{
    internal class OutputConverter<TInput> : IObjectToTypeConverter<IStorageTable>
        where TInput : class
    {
        private readonly IConverter<TInput, IStorageTable> _innerConverter;

        public OutputConverter(IConverter<TInput, IStorageTable> innerConverter)
        {
            _innerConverter = innerConverter;
        }

        public bool TryConvert(object input, out IStorageTable output)
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
