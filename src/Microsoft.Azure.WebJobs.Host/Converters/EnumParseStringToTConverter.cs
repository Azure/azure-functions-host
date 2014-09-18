// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Diagnostics;

namespace Microsoft.Azure.WebJobs.Host.Converters
{
    internal class EnumParseStringToTConverter<TOutput> : IConverter<string, TOutput>
    {
        public EnumParseStringToTConverter()
        {
            Debug.Assert(typeof(TOutput).IsEnum);
        }

        public TOutput Convert(string input)
        {
            return (TOutput)Enum.Parse(typeof(TOutput), input);
        }
    }
}
