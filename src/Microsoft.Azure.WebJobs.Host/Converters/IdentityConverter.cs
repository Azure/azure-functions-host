// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

namespace Microsoft.Azure.WebJobs.Host.Converters
{
    internal class IdentityConverter<TValue> : IConverter<TValue, TValue>
    {
        public TValue Convert(TValue input)
        {
            return input;
        }
    }
}
