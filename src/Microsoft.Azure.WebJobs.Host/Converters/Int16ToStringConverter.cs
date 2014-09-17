// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.Globalization;

namespace Microsoft.Azure.WebJobs.Host.Converters
{
    internal class Int16ToStringConverter : IConverter<short, string>
    {
        public string Convert(short input)
        {
            return input.ToString(CultureInfo.InvariantCulture);
        }
    }
}
