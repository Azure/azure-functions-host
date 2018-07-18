// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;

namespace Microsoft.Azure.WebJobs.Script.Config
{
    public class ScriptTypeLocator : ITypeLocator
    {
        private Type[] _types;

        public ScriptTypeLocator()
        {
            _types = Array.Empty<Type>();
        }

        public IReadOnlyList<Type> GetTypes()
        {
            return _types;
        }

        internal void SetTypes(IEnumerable<Type> types)
        {
            if (types == null)
            {
                throw new ArgumentNullException(nameof(types));
            }

            _types = types.ToArray();
        }
    }
}
