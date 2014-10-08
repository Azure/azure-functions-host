// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using Microsoft.Azure.WebJobs.Host.Converters;
using Microsoft.Azure.WebJobs.Host.Tables.Converters;
using Microsoft.WindowsAzure.Storage.Table;

namespace Microsoft.Azure.WebJobs.Host.Tables
{
    internal static class TToEntityPropertyConverterFactory
    {
        public static IConverter<TInput, EntityProperty> Create<TInput>()
        {
            if (typeof(TInput) == typeof(bool))
            {
                return (IConverter<TInput, EntityProperty>)new BooleanToEntityPropertyConverter();
            }
            else if (typeof(TInput) == typeof(bool?))
            {
                return (IConverter<TInput, EntityProperty>)new NullableBooleanToEntityPropertyConverter();
            }
            else if (typeof(TInput) == typeof(byte[]))
            {
                return (IConverter<TInput, EntityProperty>)new ByteArrayToEntityPropertyConverter();
            }
            else if (typeof(TInput) == typeof(DateTime))
            {
                return (IConverter<TInput, EntityProperty>)new DateTimeToEntityPropertyConverter();
            }
            else if (typeof(TInput) == typeof(DateTime?))
            {
                return (IConverter<TInput, EntityProperty>)new NullableDateTimeToEntityPropertyConverter();
            }
            else if (typeof(TInput) == typeof(DateTimeOffset))
            {
                return (IConverter<TInput, EntityProperty>)new DateTimeOffsetToEntityPropertyConverter();
            }
            else if (typeof(TInput) == typeof(DateTimeOffset?))
            {
                return (IConverter<TInput, EntityProperty>)new NullableDateTimeOffsetToEntityPropertyConverter();
            }
            else if (typeof(TInput) == typeof(double))
            {
                return (IConverter<TInput, EntityProperty>)new DoubleToEntityPropertyConverter();
            }
            else if (typeof(TInput) == typeof(double?))
            {
                return (IConverter<TInput, EntityProperty>)new NullableDoubleToEntityPropertyConverter();
            }
            else if (typeof(TInput) == typeof(Guid))
            {
                return (IConverter<TInput, EntityProperty>)new GuidToEntityPropertyConverter();
            }
            else if (typeof(TInput) == typeof(Guid?))
            {
                return (IConverter<TInput, EntityProperty>)new NullableGuidToEntityPropertyConverter();
            }
            else if (typeof(TInput) == typeof(int))
            {
                return (IConverter<TInput, EntityProperty>)new Int32ToEntityPropertyConverter();
            }
            else if (typeof(TInput) == typeof(int?))
            {
                return (IConverter<TInput, EntityProperty>)new NullableInt32ToEntityPropertyConverter();
            }
            else if (typeof(TInput) == typeof(long))
            {
                return (IConverter<TInput, EntityProperty>)new Int64ToEntityPropertyConverter();
            }
            else if (typeof(TInput) == typeof(long?))
            {
                return (IConverter<TInput, EntityProperty>)new NullableInt64ToEntityPropertyConverter();
            }
            else if (typeof(TInput) == typeof(string))
            {
                return (IConverter<TInput, EntityProperty>)new StringToEntityPropertyConverter();
            }
            else
            {
                return new PocoToEntityPropertyConverter<TInput>();
            }
        }
    }
}
