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

        public string RowKey { get; set; }

        internal class Wrapper
        {
            public TriggerReason Payload { get; set; }
        }

        // Work around JSon.Net bug:
        //   http://json.codeplex.com/workitem/22202 
        // Need $type tag on the toplevel object. So have to embed in an extra object. 
        public Wrapper Data { get; set; }
    }
}
