// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using Microsoft.Azure.WebJobs.Host.Converters;
using Microsoft.WindowsAzure.Storage.Table;

namespace Microsoft.Azure.WebJobs.Host.Tables.Converters
{
    internal class NullableEnumToEntityPropertyConverter<TEnum> : IConverter<TEnum?, EntityProperty>
        where TEnum : struct
    {
        static NullableEnumToEntityPropertyConverter()
        {
            if (!typeof(TEnum).IsEnum)
            {
                throw new InvalidOperationException("TEnum must be an Enum.");
            }
        }

        public EntityProperty Convert(TEnum? input)
        {
            if (!input.HasValue)
            {
                return EntityProperty.GeneratePropertyForString(null);
            }

            return new EntityProperty(input.Value.ToString());
        }
    }
}
