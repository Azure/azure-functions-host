// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;

namespace Microsoft.Azure.WebJobs.Host.Indexers
{
    internal class DefaultNameResolver : INameResolver
    {
        public string Resolve(string name)
        {
            throw new NotImplementedException("INameResolver must be supplied to resolve '%" + name + "%'.");
        }
    }
}
