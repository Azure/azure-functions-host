// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

namespace Microsoft.Azure.Jobs.Host.Converters
{
    internal class IdentityConverter<TValue> : IConverter<TValue, TValue>
    {
        public TValue Convert(TValue input)
        {
            return input;
        }
    }
}
