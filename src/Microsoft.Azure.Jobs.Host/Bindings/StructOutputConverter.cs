// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using Microsoft.Azure.Jobs.Host.Converters;

namespace Microsoft.Azure.Jobs.Host.Bindings
{
    internal class StructOutputConverter<TInput, TOutput> : IObjectToTypeConverter<TOutput>
        where TInput : struct
    {
        private readonly IConverter<TInput, TOutput> _innerConverter;

        public StructOutputConverter(IConverter<TInput, TOutput> innerConverter)
        {
            _innerConverter = innerConverter;
        }

        public bool TryConvert(object input, out TOutput output)
        {
            if (!(input is TInput))
            {
                output = default(TOutput);
                return false;
            }

            output = _innerConverter.Convert((TInput)input);
            return true;
        }
    }
}
