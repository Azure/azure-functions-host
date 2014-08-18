// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using Microsoft.Azure.WebJobs.Host.Converters;

namespace Microsoft.Azure.WebJobs.Host.Bindings.Data
{
    internal class TToStringConverter<TInput> : IConverter<TInput, string>
    {
        public string Convert(TInput input)
        {
            return input.ToString();
        }
    }
}
