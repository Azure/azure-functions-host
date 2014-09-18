// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Globalization;

namespace Microsoft.Azure.WebJobs.Host.Converters
{
    internal class TryParseStringToTConverter<TOutput> : IConverter<string, TOutput>
    {
        TryParseDelegate<TOutput> _tryParseDelegate;

        public TryParseStringToTConverter(TryParseDelegate<TOutput> tryParse)
        {
            _tryParseDelegate = tryParse;
        }

        public TOutput Convert(string input)
        {
            TOutput parsed;

            if (!_tryParseDelegate.Invoke(input, out parsed))
            {
                string msg = String.Format(CultureInfo.CurrentCulture,
                    "Parameter is illegal format to parse as type '{0}'", typeof(TOutput).FullName);
                throw new InvalidOperationException(msg);
            }

            return parsed;
        }
    }
}
