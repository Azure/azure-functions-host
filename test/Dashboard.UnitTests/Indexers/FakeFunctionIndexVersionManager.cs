// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using Dashboard.Data;

namespace Dashboard.UnitTests.Indexers
{
    internal class FakeFunctionIndexVersionManager : IFunctionIndexVersionManager
    {
        public DateTimeOffset Current { get; set; }

        public void UpdateOrCreateIfLatest(DateTimeOffset version)
        {
            if (version > Current)
            {
                Current = version;
            }
        }
    }
}
