// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using Microsoft.Azure.WebJobs.Host.Converters;
using Microsoft.WindowsAzure.Storage.Table;

namespace Microsoft.Azure.WebJobs.Host.Tables.Converters
{
    internal class EntityPropertyToEnumConverter<TProperty> : IConverter<EntityProperty, TProperty>
    {
        static EntityPropertyToEnumConverter()
        {
            if (!typeof(TProperty).IsEnum)
            {
                throw new InvalidOperationException("TProperty must be an Enum.");
            }
        }

        public TProperty Convert(EntityProperty input)
        {
            if (input == null)
            {
                throw new ArgumentNullException("input");
            }

            string unparsed = input.StringValue;

            if (unparsed == null)
            {
                throw new InvalidOperationException("Enum property value must not be null.");
            }

            return (TProperty)Enum.Parse(typeof(TProperty), unparsed);
        }
    }
}
