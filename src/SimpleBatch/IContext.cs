using System;

namespace Microsoft.WindowsAzure.Jobs
{
    // Get the function instance guid for the currently executing function 
    internal interface IContext
    {
        Guid FunctionInstanceGuid { get; }
    }
}
