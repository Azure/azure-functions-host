using System;
using System.Collections.Generic;

namespace Microsoft.WindowsAzure.Jobs
{
    // Service to get the types that may contain SimpleBatch methods. 
    internal interface ITypeLocator
    {        
        IEnumerable<Type> FindTypes();
    }
}