// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Text;

namespace Microsoft.Azure.Functions.Analyzers
{
    // Represent a bound generic type like:
    //   IEnumerable<string>
    // definition is IEnumerable`1, and TypeArgs are [string]
    class GenericFakeType : FakeType
    {
        private readonly Type _typeDefinition; // This can be a real type
        private readonly Type[] _typeArgs;

        public GenericFakeType(Type typeDefinition, Type[] typeArgs)
            : base(null, null, null)
        {
            _typeDefinition = typeDefinition;
            _typeArgs = typeArgs;
        }

        public override bool IsGenericType => true;

        public override Type GetGenericTypeDefinition()
        {
            return _typeDefinition;
        }

        public override Type[] GetGenericArguments()
        {
            return _typeArgs;
        }

        public override string ToString()
        {
            StringBuilder sb = new StringBuilder();
            sb.Append(_typeDefinition.Name);
            sb.Append('<');

            bool first = true;
            foreach (var x in _typeArgs)
            {
                if (!first)
                {
                    sb.Append(",");
                }
                first = false;
                sb.Append(x.ToString());
            }
            sb.Append('>');
            return sb.ToString();
        }
    }
}
