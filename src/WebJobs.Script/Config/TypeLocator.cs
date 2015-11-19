// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;

namespace Microsoft.Azure.WebJobs.Script
{
    public class TypeLocator : ITypeLocator
    {
        private Type[] _types;

        public TypeLocator(Type type)
        {
            _types = new Type[] { type };
        }

        public TypeLocator(params Type[] types)
        {
            _types = types;
        }

        public TypeLocator(IEnumerable<Type> types)
        {
            _types = types.ToArray();
        }

        public IReadOnlyList<Type> GetTypes()
        {
            return _types;
        }
    }
}
