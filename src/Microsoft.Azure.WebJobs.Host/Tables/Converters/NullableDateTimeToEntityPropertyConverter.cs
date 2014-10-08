// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using Microsoft.Azure.WebJobs.Host.Converters;
using Microsoft.WindowsAzure.Storage.Table;

namespace Microsoft.Azure.WebJobs.Host.Tables.Converters
{
    internal class NullableDateTimeToEntityPropertyConverter : IConverter<DateTime?, EntityProperty>
    {
        public EntityProperty Convert(DateTime? input)
        {
            if (input.HasValue)
            {
                DateTimeToEntityPropertyConverter.ThrowIfUnsupportedValue(input.Value);
            }

            return new EntityProperty(input);
        }
    }
}
