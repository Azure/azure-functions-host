// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using Microsoft.Azure.WebJobs.Host.Converters;
using Microsoft.Azure.WebJobs.Host.Tables.Converters;
using Microsoft.WindowsAzure.Storage.Table;

namespace Microsoft.Azure.WebJobs.Host.Tables
{
    internal static class EntityPropertyToTConverterFactory
    {
        public static IConverter<EntityProperty, TOutput> Create<TOutput>()
        {
            if (typeof(TOutput) == typeof(bool))
            {
                return (IConverter<EntityProperty, TOutput>)new EntityPropertyToBooleanConverter();
            }
            else if (typeof(TOutput) == typeof(bool?))
            {
                return (IConverter<EntityProperty, TOutput>)new EntityPropertyToNullableBooleanConverter();
            }
            else if (typeof(TOutput) == typeof(byte[]))
            {
                return (IConverter<EntityProperty, TOutput>)new EntityPropertyToByteArrayConverter();
            }
            else if (typeof(TOutput) == typeof(DateTime))
            {
                return (IConverter<EntityProperty, TOutput>)new EntityPropertyToDateTimeConverter();
            }
            else if (typeof(TOutput) == typeof(DateTime?))
            {
                return (IConverter<EntityProperty, TOutput>)new EntityPropertyToNullableDateTimeConverter();
            }
            else if (typeof(TOutput) == typeof(DateTimeOffset))
            {
                return (IConverter<EntityProperty, TOutput>)new EntityPropertyToDateTimeOffsetConverter();
            }
            else if (typeof(TOutput) == typeof(DateTimeOffset?))
            {
                return (IConverter<EntityProperty, TOutput>)new EntityPropertyToNullableDateTimeOffsetConverter();
            }
            else if (typeof(TOutput) == typeof(double))
            {
                return (IConverter<EntityProperty, TOutput>)new EntityPropertyToDoubleConverter();
            }
            else if (typeof(TOutput) == typeof(double?))
            {
                return (IConverter<EntityProperty, TOutput>)new EntityPropertyToNullableDoubleConverter();
            }
            else if (typeof(TOutput) == typeof(Guid))
            {
                return (IConverter<EntityProperty, TOutput>)new EntityPropertyToGuidConverter();
            }
            else if (typeof(TOutput) == typeof(Guid?))
            {
                return (IConverter<EntityProperty, TOutput>)new EntityPropertyToNullableGuidConverter();
            }
            else if (typeof(TOutput) == typeof(int))
            {
                return (IConverter<EntityProperty, TOutput>)new EntityPropertyToInt32Converter();
            }
            else if (typeof(TOutput) == typeof(int?))
            {
                return (IConverter<EntityProperty, TOutput>)new EntityPropertyToNullableInt32Converter();
            }
            else if (typeof(TOutput) == typeof(long))
            {
                return (IConverter<EntityProperty, TOutput>)new EntityPropertyToInt64Converter();
            }
            else if (typeof(TOutput) == typeof(long?))
            {
                return (IConverter<EntityProperty, TOutput>)new EntityPropertyToNullableInt64Converter();
            }
            else if (typeof(TOutput) == typeof(string))
            {
                return (IConverter<EntityProperty, TOutput>)new EntityPropertyToStringConverter();
            }
            else
            {
                return new EntityPropertyToPocoConverter<TOutput>();
            }
        }
    }
}
