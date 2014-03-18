using System;

namespace Microsoft.WindowsAzure.Jobs.Host.Runners
{
    internal class FunctionInstanceLookup : IFunctionInstanceLookup
    {
        // Lookup in primary index
        // $$$ Should this be IFunctionInstanceLookup instead?
        private readonly IAzureTableReader<ExecutionInstanceLogEntity> _tableLookup;

        public FunctionInstanceLookup(IAzureTableReader<ExecutionInstanceLogEntity> tableLookup)
        {
            if (tableLookup == null)
            {
                throw new ArgumentNullException("tableLookup");
            }

            _tableLookup = tableLookup;
        }

        ExecutionInstanceLogEntity IFunctionInstanceLookup.Lookup(Guid rowKey)
        {
            return FunctionUpdatedLogger.RawLookup(_tableLookup, rowKey.ToString());
        }
   }
}
