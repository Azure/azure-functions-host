// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;

namespace Microsoft.Azure.Functions.Analyzers
{
    class ArrayType : FakeType
    {
        private readonly Type _inner;
        public ArrayType(Type inner)
            : base(null, null, null)
        {
            _inner = inner;
        }

        protected override bool IsArrayImpl()
        {
            return true;
        }

        public override Type GetElementType()
        {
            return _inner;
        }

        public override string ToString()
        {
            return _inner + "[]";
        }
    }
}
