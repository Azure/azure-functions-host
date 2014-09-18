// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.ComponentModel;
using System.Diagnostics;

namespace Microsoft.Azure.WebJobs.Host.Converters
{
    internal class TypeConverterStringToTConverter<TOutput> : IConverter<string, TOutput>
    {
        private readonly TypeConverter _typeConverter;

        public TypeConverterStringToTConverter(TypeConverter typeConverter)
        {
            _typeConverter = typeConverter;
            Debug.Assert(typeConverter.CanConvertFrom(typeof(string)));
            Debug.Assert(typeConverter.CanConvertTo(typeof(TOutput)));
        }

        public TOutput Convert(string input)
        {
            return (TOutput)_typeConverter.ConvertFrom(input);
        }
    }
}
