// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using Microsoft.Azure.WebJobs.Host.Converters;

namespace Microsoft.Azure.WebJobs.Host.Bindings
{
    internal class StringToTConverter<TOutput> : IConverter<string, TOutput>
    {
        public TOutput Convert(string input)
        {
            return (TOutput)ObjectBinderHelpers.BindFromString(input, typeof(TOutput));
        }
    }
}
