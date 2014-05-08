using System;

namespace Microsoft.Azure.Jobs.Host.UnitTests
{
    internal static class FunctionInstanceLookupExtensions
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
}
