using System;
using System.Collections.Generic;

namespace Microsoft.Azure.Jobs
{
    internal class Configuration : IConfiguration
    {
        private IList<Type> _cloudBlobStreamBinderTypes = new List<Type>();

        public IList<Type> CloudBlobStreamBinderTypes
        {
            get { return _cloudBlobStreamBinderTypes; }
        }

        public INameResolver NameResolver { get; set; }
    }
}
