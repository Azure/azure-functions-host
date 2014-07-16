// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;

namespace Microsoft.Azure.Jobs.Host.Indexers
{
    internal class DefaultNameResolver : INameResolver
    {
        public string Resolve(string name)
        {
            throw new NotImplementedException("INameResolver must be supplied to resolve '%" + name + "%'.");
        }
    }
}
