using System;

namespace Microsoft.Azure.Jobs.Host.IntegrationTests
{
    internal static class FunctionInstanceLookupExtensions
    {
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
