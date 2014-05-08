using System;
using Newtonsoft.Json;

namespace Microsoft.Azure.Jobs.Host.UnitTests
{
    // Primary access to the azure table storing function invoke requests.  
    internal class ExecutionInstanceLogEntitySerializer
    {
        // Partition key is a constant. Row key is ExecutionInstanceLogEntity.GetKey(), which is the function instance Guid. 
        private readonly IAzureTable<ExecutionInstanceLogEntity> _table;

        const string PartKey = "1";

        public ExecutionInstanceLogEntitySerializer(IAzureTable<ExecutionInstanceLogEntity> table)
        {
            if (table == null)
            {
                throw new ArgumentNullException("table");
            }
            _table = table;
        }

        // This may be called multiple times as a function execution is processed (queued, exectuing, completed, etc)
        public void Log(ExecutionInstanceLogEntity log)
        {
            string rowKey = log.GetKey();
            var l2 = Read(rowKey);
            if (l2 == null)
            {
                l2 = log;
            }
            else
            {
                // Merge
                Merge(l2, log);
            }

            _table.Write(PartKey, rowKey, l2);
            _table.Flush();
        }

        public ExecutionInstanceLogEntity Read(Guid id)
        {
            return Read(id.ToString());
        }

        public ExecutionInstanceLogEntity Read(string rowKey)
        {
            try
            {
                ExecutionInstanceLogEntity func = _table.Lookup(PartKey, rowKey);
                return func;
            }
            catch (JsonSerializationException)
            {
                // Likely failed to deserialize, which means stale data. Just ignore it. 
                return null;
            }
        }

        // $$$ Should be a merge. Move this merge operation in IAzureTable?
        public static void Merge<T>(T mutate, T delta)
        {
            foreach (var property in typeof(T).GetProperties())
            {
                var deltaVal = property.GetValue(delta, null);
                if (deltaVal != null)
                {
                    property.SetValue(mutate, deltaVal, null);
                }
            }
        }
    }
}