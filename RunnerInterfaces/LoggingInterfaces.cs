using System;
using System.Collections.Generic;
using Executor;

namespace RunnerInterfaces
{
    // Log when a function has completed.
    // This can also write and secondary indices needed for querying the logs. 
    // Called by the Orchestrator. 
    public interface IFunctionCompleteLogger
    {
        // Called by the orchestrator (which gaurantees single-threaded access) sometime shortly after a 
        // function finishes executing.
        void IndexCompletedFunction(ExecutionInstanceLogEntity func);

        void Flush();
    }

    // Log as an individual function is getting updated. 
    // This may be called multiple times as a function execution is processed (queued, exectuing, completed, etc)
    // Called by whatever node "owns" the function (usually the executor).
    public interface IFunctionUpdatedLogger
    {
        void Log(ExecutionInstanceLogEntity func);
    }

    // Looking up a function instance given the key. 
    // Guid is the FunctionInstance identifier
    // Called by any node, after function has been provided by IFunctionUpdatedLogger.
    public interface IFunctionInstanceLookup
    {
        ExecutionInstanceLogEntity Lookup(Guid rowKey);
    }

    public static class IFunctionInstanceLookupExtensions
    {
        static ExecutionInstanceLogEntity Lookup(this IFunctionInstanceLookup lookup, FunctionInstance instance)
        {
            return lookup.Lookup(instance.Id);
        }
    }

    // Ammends lookup with query functionality 
    // This may use secondary indices built by IFunctionCompleteLogger.
    // Functions don't show up here until after they've been indexed by IFunctionCompleteLogger.
    // $$$ May need to change from IEnumerable to support pagination + querying?
    public interface IFunctionInstanceQuery : IFunctionInstanceLookup
    {
        IEnumerable<ExecutionInstanceLogEntity> GetRecent(int N, FunctionInstanceQueryFilter filter);
    }

    // $$$ Filter to storage container? Function, Date? Success status? Becomes a full-fledged database!
    public class FunctionInstanceQueryFilter
    {
        // Only return functions in the given account name
        public string AccountName { get; set; }

        public FunctionLocation Location { get; set; }

        // If has value, the filter whether function has completed and succeeded. 
        // Does not include Queued functions or Running functions. 
        // $$$ Change to FunctionInstanceStatus and include those?
        public bool? Succeeded;      
    }
}