// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Globalization;

namespace Microsoft.Azure.WebJobs.Host.Converters
{
    internal class StringToUInt64Converter : IConverter<string, ulong>
    {
        public ulong Convert(string input)
        {
            return UInt64.Parse(input, NumberStyles.None, CultureInfo.InvariantCulture);
        }
    }
}
