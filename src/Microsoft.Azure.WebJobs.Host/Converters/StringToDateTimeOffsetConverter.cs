// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Globalization;

namespace Microsoft.Azure.WebJobs.Host.Converters
{
    internal class StringToDateTimeOffsetConverter : IConverter<string, DateTimeOffset>
    {
        public DateTimeOffset Convert(string input)
        {
            return DateTimeOffset.ParseExact(input, "O", CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal);
        }
    }
}
