// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using Microsoft.Azure.WebJobs.Host.Converters;
using Microsoft.WindowsAzure.Storage.Table;

namespace Microsoft.Azure.WebJobs.Host.Tables.Converters
{
    internal class EntityPropertyToNullableEnumConverter<TEnum> : IConverter<EntityProperty, TEnum?>
        where TEnum : struct
    {
        static EntityPropertyToNullableEnumConverter()
        {
            if (!typeof(TEnum).IsEnum)
            {
                throw new InvalidOperationException("TEnum must be an Enum.");
            }
        }

        public TEnum? Convert(EntityProperty input)
        {
            if (input == null)
            {
                throw new ArgumentNullException("input");
            }

            string unparsed = input.StringValue;

            if (unparsed == null)
            {
                return null;
            }

            return (TEnum)Enum.Parse(typeof(TEnum), unparsed);
        }
    }
}
