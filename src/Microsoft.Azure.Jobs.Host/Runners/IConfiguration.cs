using System;
using System.Collections.Generic;

namespace Microsoft.Azure.Jobs
{
    internal interface IConfiguration
    {
        IList<Type> CloudBlobStreamBinderTypes { get; }

        INameResolver NameResolver { get; set; }
    }
}
