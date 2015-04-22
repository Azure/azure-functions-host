// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;

namespace Microsoft.Azure.WebJobs.Host.Indexers
{
    internal interface IExtensionTypeLocator
    {
        IReadOnlyList<Type> GetCloudBlobStreamBinderTypes();
    }
}
