using System;
using Microsoft.WindowsAzure.Storage.Table.DataServices;

namespace Microsoft.Azure.Jobs.Host.Protocols
{
    /// <summary>
    /// Represents an entry in the secondary-index tables that are used to 
    /// look up function invocations per job, per status, per function, etc.
    /// </summary>
    /// <remarks>
    /// It abstracts away the three different shapes we currently have for indexing function invocations:
    /// 1. MRU tables, that has the target invocation id as a STRING column named 'Value'
    /// 2. InJob index table, that has the target invocation id as a GUID column named 'InvocationId'
    /// 3. CausalityLog  table, that has the target invocation id as a field called "ChildGuid" within a Json Serialized complex object named 'Data'
    ///
    /// in Future releases we will have the dashboard percolate and clean the raw data sent from the host, in and then we will converge all
    /// invocation indexes to a common shape, and normalize this class.
    /// </remarks>
    internal class FunctionInvocationIndexEntity : TableServiceEntity
    {
        public Guid InvocationId { get; set; }

        // the Value and Data are to make it work with non-normalized indexes (the older MRU tables and the CausalityLog)
        // until we normalize all invocation indexes tables to have a Guid field pointing at invocations
        private string _value;
        private string _data;

        public string Value
        {
            get { return _value; }
            set
            {
                _value = value;
                Guid invocationId;
                if (Guid.TryParse(value, out invocationId))
                {
                    InvocationId = invocationId;
                }
            }
        }

        public string Data
        {
            get { return _data; }
            set
            {
                _data = value;
                if (value != null)
                {
                    var x = JsonCustom.DeserializeObject<TriggerReasonEntity.Wrapper>(value);
                    InvocationId = x.Payload.ChildGuid;
                }
            }
        }
    }
}