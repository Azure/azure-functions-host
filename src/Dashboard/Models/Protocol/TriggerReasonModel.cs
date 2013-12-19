using System;
using Microsoft.WindowsAzure.Jobs;

namespace Dashboard.Models.Protocol
{
    public class TriggerReasonModel
    {
        internal TriggerReason UnderlyingObject { get; private set; }

        internal TriggerReasonModel(TriggerReason underlyingObject)
        {
            UnderlyingObject = underlyingObject;
        }

        public Guid ParentGuid
        {
            get { return UnderlyingObject.ParentGuid; }
        }

        public Guid ChildGuid
        {
            get { return UnderlyingObject.ChildGuid; }
        }

        public override string ToString()
        {
            return UnderlyingObject.ToString();
        }
    }
}
