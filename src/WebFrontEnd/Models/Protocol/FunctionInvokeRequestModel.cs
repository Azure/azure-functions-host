using System;

namespace Microsoft.WindowsAzure.Jobs.Dashboard.Models.Protocol
{
    public class FunctionInvokeRequestModel
    {
        internal FunctionInvokeRequest UnderlyingObject { get; private set; }

        internal FunctionInvokeRequestModel(FunctionInvokeRequest underlyingObject)
        {
            UnderlyingObject = underlyingObject;
            TriggerReason = new TriggerReasonModel(underlyingObject.TriggerReason);
            Location = new FunctionLocationModel(underlyingObject.Location);
        }

        public TriggerReasonModel TriggerReason { get; private set; }

        public FunctionLocationModel Location { get; private set; }

        public Guid Id { get { return UnderlyingObject.Id; } }

        public override string ToString()
        {
            return UnderlyingObject.ToString();
        }
    }
}