// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;

namespace Microsoft.Azure.WebJobs.Host.FunctionalTests
{
    internal class FakeTypeLocator : ITypeLocator
    {
        private readonly IReadOnlyList<Type> _types;

        public FakeTypeLocator(params Type[] types)
        {
            _types = types;
        }

        public IReadOnlyList<Type> GetTypes()
        {
            return _types;
        }
    }
}
