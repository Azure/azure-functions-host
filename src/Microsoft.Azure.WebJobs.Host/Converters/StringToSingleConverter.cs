// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Globalization;

namespace Microsoft.Azure.WebJobs.Host.Converters
{
    internal class StringToSingleConverter : IConverter<string, float>
    {
        public float Convert(string input)
        {
            const NumberStyles floatWithoutWhitespace = NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint |
                NumberStyles.AllowExponent;

            return Single.Parse(input, floatWithoutWhitespace, CultureInfo.InvariantCulture);
        }
    }
}
