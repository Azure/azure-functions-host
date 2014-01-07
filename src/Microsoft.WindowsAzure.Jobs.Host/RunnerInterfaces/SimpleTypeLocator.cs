using System;
using System.Collections.Generic;

namespace Microsoft.WindowsAzure.Jobs
{
    internal class SimpleTypeLocator : ITypeLocator
    {
        private readonly Type[] _types;

        public SimpleTypeLocator(params Type[] types)
        {
            _types = types;
        }

        public IEnumerable<Type> FindTypes()
        {
            return _types;
        }
    }
}