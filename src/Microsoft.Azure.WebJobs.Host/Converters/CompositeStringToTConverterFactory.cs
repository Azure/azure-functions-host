// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

namespace Microsoft.Azure.WebJobs.Host.Converters
{
    internal class CompositeStringToTConverterFactory : IStringToTConverterFactory
    {
        private readonly IStringToTConverterFactory[] _factories;

        public CompositeStringToTConverterFactory(params IStringToTConverterFactory[] factories)
        {
            _factories = factories;
        }

        public IConverter<string, TOutput> TryCreate<TOutput>()
        {
            foreach (IStringToTConverterFactory factory in _factories)
            {
                IConverter<string, TOutput> possibleFactory = factory.TryCreate<TOutput>();

                if (possibleFactory != null)
                {
                    return possibleFactory;
                }
            }

            return null;
        }
    }
}
