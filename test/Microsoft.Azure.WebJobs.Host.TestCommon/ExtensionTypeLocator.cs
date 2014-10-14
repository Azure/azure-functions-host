// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using Microsoft.Azure.WebJobs.Host.Indexers;

namespace Microsoft.Azure.WebJobs.Host.TestCommon
{
    public class NullExtensionTypeLocator : IExtensionTypeLocator
    {
        public IReadOnlyList<Type> GetCloudBlobStreamBinderTypes()
        {
            return new Type[0];
        }
    }
}
