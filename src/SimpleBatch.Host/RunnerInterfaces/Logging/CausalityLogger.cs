using System;
using System.Collections.Generic;
using System.Linq;


namespace Microsoft.WindowsAzure.Jobs
{
    // For table access, have a level of indirection so that the TriggerReason is serialized
    // as a JSON object, which then supports polymorphism when we deserialize. 
    // This serailized TriggerReason as a single column (with JSON) rather than a column per property of TriggerReason. 
    internal class TriggerReasonEntity
    {
        public TriggerReasonEntity() { }
        public TriggerReasonEntity(TriggerReason payload)
        {
            this.Data = new Wrapper { Payload = payload };
        }

        internal class Wrapper
        {
            public TriggerReason Payload { get; set; }
        }
        
        // Work around JSon.Net bug:
        //   http://json.codeplex.com/workitem/22202 
        // Need $type tag on the toplevel object. So have to embed in an extra object. 
        public Wrapper Data { get; set; }
    }
        
    // Implements the causality logger interfaces
    internal class CausalityLogger : ICausalityLogger, ICausalityReader
    {
        private readonly IAzureTable<TriggerReasonEntity> _table;
        private readonly IFunctionInstanceLookup _logger; // needed to lookup parents

        public CausalityLogger(IAzureTable<TriggerReasonEntity> table, IFunctionInstanceLookup logger)
        {
            _table = table;
            _logger = logger;
        }

        // ICausalityLogger
        public void LogTriggerReason(TriggerReason reason)
        {
            if (reason == null)
            {
                throw new ArgumentNullException("reason");
            }

            if (reason.ParentGuid == Guid.Empty)
            {
                // Nothing to log. 
                return;
            }

            if (reason.ChildGuid == Guid.Empty)
            {
                throw new InvalidOperationException("Child guid must be set.");
            }

            string rowKey = Utility.GetTickRowKey();
            string partitionKey  = reason.ParentGuid.ToString();
            var value = new TriggerReasonEntity(reason);
            _table.Write(partitionKey, rowKey, values: value);
            _table.Flush();
        }

        // ICausalityReader
        public IEnumerable<TriggerReason> GetChildren(Guid parent)
        {
            var value = from val in _table.Enumerate(parent.ToString()) select val.Data.Payload;
            var array = value.ToArray();
            return array;
        }


        public Guid GetParent(Guid child)
        {
            if (_logger == null)
            {
                throw new InvalidOperationException("In Write-only mode.");  
            }
            var entry = _logger.Lookup(child);
            TriggerReason reason = entry.FunctionInstance.TriggerReason;
            
            return reason.ParentGuid;
        }
    }
}