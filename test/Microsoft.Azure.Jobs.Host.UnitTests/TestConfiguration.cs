using System;
using System.Collections.Generic;

namespace Microsoft.Azure.Jobs.Host.UnitTests
{
    internal class TestConfiguration : IConfiguration
    {
        public IList<Type> CloudBlobStreamBinderTypes
        {
            get { return new Type[0]; }
        }

        public INameResolver NameResolver { get; set; }
    }
}
