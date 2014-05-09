using System;

namespace Microsoft.Azure.Jobs.Host.IntegrationTests
{
    // Looking up a function instance given the key. 
    // Guid is the FunctionInstance identifier
    // Called by any node, after function has been provided by IFunctionUpdatedLogger.
    internal interface IFunctionInstanceLookup
    {
        // $$$ Can this return null?
        ExecutionInstanceLogEntity Lookup(Guid rowKey);
    }
}
