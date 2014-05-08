using System;
using Microsoft.Azure.Jobs;
using Newtonsoft.Json;

namespace Dashboard.Data
{
    internal class FunctionInstanceLookup : IFunctionInstanceLookup
    {
        private const string PartKey = "1";

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
            return RawLookup(_tableLookup, rowKey.ToString());
        }

        internal static ExecutionInstanceLogEntity RawLookup(IAzureTableReader<ExecutionInstanceLogEntity> table, string rowKey)
        {
            try
            {
                ExecutionInstanceLogEntity func = table.Lookup(PartKey, rowKey);
                return func;
            }
            catch (JsonSerializationException)
            {
                // Likely failed to deserialize, which means stale data. Just ignore it. 
                return null;
            }
        }
   }
}
