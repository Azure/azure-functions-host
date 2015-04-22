// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using Microsoft.Azure.WebJobs.Host.Converters;
using Microsoft.WindowsAzure.Storage.Table;

namespace Microsoft.Azure.WebJobs.Host.Tables.Converters
{
    internal class EnumToEntityPropertyConverter<TEnum> : IConverter<TEnum, EntityProperty>
    {
        static EnumToEntityPropertyConverter()
        {
            if (!typeof(TEnum).IsEnum)
            {
                throw new InvalidOperationException("TProperty must be an Enum.");
            }
        }

        public EntityProperty Convert(TEnum input)
        {
            return new EntityProperty(input.ToString());
        }
    }
}
