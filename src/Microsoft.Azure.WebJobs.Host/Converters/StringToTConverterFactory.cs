// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

namespace Microsoft.Azure.WebJobs.Host.Converters
{
    internal static class StringToTConverterFactory
    {
        private static readonly IStringToTConverterFactory _instance = new CompositeStringToTConverterFactory(
            new IdentityStringToTConverterFactory(),
            new KnownTypesParseToStringConverterFactory(),
            new TryParseStringToTConverterFactory(),
            new TypeConverterStringToTConverterFactory());

        public static IStringToTConverterFactory Instance
        {
            get { return _instance; }
        }
    }
}
