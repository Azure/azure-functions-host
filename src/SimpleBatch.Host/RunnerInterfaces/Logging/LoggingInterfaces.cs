using System;
using System.Collections.Generic;
using Executor;

namespace RunnerInterfaces
{
    internal interface IFunctionInstanceLogger
    {
        IFunctionInstanceLoggerContext CreateContext(ExecutionInstanceLogEntity func);

        void Flush();
    }

    internal interface IFunctionInstanceLoggerContext
    {
        void IndexRunningFunction();

        void IndexCompletedFunction();

        void Flush();
    }

    // Log as an individual function is getting updated. 
    // This may be called multiple times as a function execution is processed (queued, exectuing, completed, etc)
    // Called by whatever node "owns" the function (usually the executor).
    internal interface IFunctionUpdatedLogger
    {
        // The func can be partially filled out, and this will merge non-null fields onto the log. 

        // $$$ Beware, this encourages partially filled out ExecutionInstanceLogEntity to be floating around,
        //  which may cause confusion elsewhere (eg, wrong results from GetStatus). 
        //  Should this update func in place to be the latest results?
        void Log(ExecutionInstanceLogEntity func);
    }

    // Looking up a function instance given the key. 
    // Guid is the FunctionInstance identifier
    // Called by any node, after function has been provided by IFunctionUpdatedLogger.
    internal interface IFunctionInstanceLookup
    {
        // $$$ Can this return null?
        ExecutionInstanceLogEntity Lookup(Guid rowKey);
    }

    internal static class IFunctionInstanceLookupExtensions
    {
        public static ExecutionInstanceLogEntity Lookup(this IFunctionInstanceLookup lookup, FunctionInvokeRequest instance)
        {
            return lookup.Lookup(instance.Id);
        }

        public static ExecutionInstanceLogEntity LookupOrThrow(this IFunctionInstanceLookup lookup, Guid rowKey)
        {
            var logItem = lookup.Lookup(rowKey);

            if (logItem == null)
            {
                throw new InvalidOperationException("Function guid not found: " + rowKey.ToString());
            }
            return logItem;
        }
    }

    // Ammends lookup with query functionality 
    // This may use secondary indices built by IFunctionCompleteLogger.
    // Functions don't show up here until after they've been indexed by IFunctionCompleteLogger.
    // $$$ May need to change from IEnumerable to support pagination + querying?
    internal interface IFunctionInstanceQuery : IFunctionInstanceLookup
    {
        IEnumerable<ExecutionInstanceLogEntity> GetRecent(int N, FunctionInstanceQueryFilter filter);
    }

    // $$$ Filter to storage container? Function, Date? Success status? Becomes a full-fledged database!
    internal class FunctionInstanceQueryFilter
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