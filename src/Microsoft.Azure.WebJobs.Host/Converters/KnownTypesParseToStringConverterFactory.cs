// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Numerics;

namespace Microsoft.Azure.WebJobs.Host.Converters
{
    internal class KnownTypesParseToStringConverterFactory : IStringToTConverterFactory
    {
        public IConverter<string, TOutput> TryCreate<TOutput>()
        {
            if (typeof(TOutput) == typeof(byte))
            {
                return (IConverter<string, TOutput>)new StringToByteConverter();
            }
            else if (typeof(TOutput) == typeof(sbyte))
            {
                return (IConverter<string, TOutput>)new StringToSByteConverter();
            }
            else if (typeof(TOutput) == typeof(short))
            {
                return (IConverter<string, TOutput>)new StringToInt16Converter();
            }
            else if (typeof(TOutput) == typeof(ushort))
            {
                return (IConverter<string, TOutput>)new StringToUInt16Converter();
            }
            else if (typeof(TOutput) == typeof(int))
            {
                return (IConverter<string, TOutput>)new StringToInt32Converter();
            }
            else if (typeof(TOutput) == typeof(uint))
            {
                return (IConverter<string, TOutput>)new StringToUInt32Converter();
            }
            else if (typeof(TOutput) == typeof(long))
            {
                return (IConverter<string, TOutput>)new StringToInt64Converter();
            }
            else if (typeof(TOutput) == typeof(ulong))
            {
                return (IConverter<string, TOutput>)new StringToUInt64Converter();
            }
            else if (typeof(TOutput) == typeof(float))
            {
                return (IConverter<string, TOutput>)new StringToSingleConverter();
            }
            else if (typeof(TOutput) == typeof(double))
            {
                return (IConverter<string, TOutput>)new StringToDoubleConverter();
            }
            else if (typeof(TOutput) == typeof(decimal))
            {
                return (IConverter<string, TOutput>)new StringToDecimalConverter();
            }
            else if (typeof(TOutput) == typeof(BigInteger))
            {
                return (IConverter<string, TOutput>)new StringToBigIntegerConverter();
            }
            else if (typeof(TOutput) == typeof(Guid))
            {
                return (IConverter<string, TOutput>)new StringToGuidConverter();
            }
            else if (typeof(TOutput) == typeof(DateTime))
            {
                return (IConverter<string, TOutput>)new StringToDateTimeConverter();
            }
            else if (typeof(TOutput) == typeof(DateTimeOffset))
            {
                return (IConverter<string, TOutput>)new StringToDateTimeOffsetConverter();
            }
            else if (typeof(TOutput) == typeof(TimeSpan))
            {
                return (IConverter<string, TOutput>)new StringToTimeSpanConverter();
            }
            else
            {
                return null;
            }
        }
    }
}
