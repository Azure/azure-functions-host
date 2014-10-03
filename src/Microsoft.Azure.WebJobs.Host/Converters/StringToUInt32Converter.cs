// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Globalization;

namespace Microsoft.Azure.WebJobs.Host.Converters
{
    internal class StringToUInt32Converter : IConverter<string, uint>
    {
        public uint Convert(string input)
        {
            return UInt32.Parse(input, NumberStyles.None, CultureInfo.InvariantCulture);
        }
    }
}
