// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

namespace Microsoft.Azure.WebJobs.Host.Converters
{
    internal interface IObjectToTypeConverter<TOutput>
    {
        bool TryConvert(object input, out TOutput output);
    }
}
