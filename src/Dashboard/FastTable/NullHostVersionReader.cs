// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

namespace Dashboard.Data
{
    internal class NullHostVersionReader : IHostVersionReader
    {
        // contract is to only report unsupported versions. 
        public HostVersion[] ReadAll()
        {
            return new HostVersion[] { };
        }
    }
}