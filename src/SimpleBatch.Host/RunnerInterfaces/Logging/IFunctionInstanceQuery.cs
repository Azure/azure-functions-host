using System.Collections.Generic;

namespace Microsoft.WindowsAzure.Jobs
{
    // Ammends lookup with query functionality 
    // This may use secondary indices built by IFunctionCompleteLogger.
    // Functions don't show up here until after they've been indexed by IFunctionCompleteLogger.
    // $$$ May need to change from IEnumerable to support pagination + querying?
    internal interface IFunctionInstanceQuery : IFunctionInstanceLookup
    {
        IEnumerable<ExecutionInstanceLogEntity> GetRecent(int N, FunctionInstanceQueryFilter filter);
    }
}
