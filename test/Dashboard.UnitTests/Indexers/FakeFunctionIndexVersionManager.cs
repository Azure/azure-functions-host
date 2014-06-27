// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

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
