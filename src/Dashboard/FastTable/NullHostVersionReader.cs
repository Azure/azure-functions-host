// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

namespace Dashboard.Data
{
    internal class NullHostVersionReader : IHostVersionReader
    {
        public HostVersion[] ReadAll()
        {
            return new HostVersion[]
                {
                     new HostVersion { Label = "Azure", Link = "http://Azure.com" }
                };
        }
    }
}