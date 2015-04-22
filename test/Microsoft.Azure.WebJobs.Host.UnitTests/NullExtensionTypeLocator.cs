// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using Microsoft.Azure.WebJobs.Host.Indexers;

namespace Microsoft.Azure.WebJobs.Host.UnitTests
{
    internal class NullExtensionTypeLocator : IExtensionTypeLocator
    {
        public IReadOnlyList<Type> GetCloudBlobStreamBinderTypes()
        {
            return new Type[0];
        }
    }
}
