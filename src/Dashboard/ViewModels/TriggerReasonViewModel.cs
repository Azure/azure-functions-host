using System;
using Dashboard.Data;
using Microsoft.Azure.Jobs;

namespace Dashboard.ViewModels
{
    public class TriggerReasonViewModel
    {
        internal FunctionInstanceSnapshot UnderlyingObject { get; private set; }

        internal TriggerReasonViewModel(FunctionInstanceSnapshot underlyingObject)
        {
            UnderlyingObject = underlyingObject;
        }

        public Guid ParentGuid
        {
            get { return UnderlyingObject.ParentId.HasValue ? UnderlyingObject.ParentId.Value : Guid.Empty; }
        }

        public Guid ChildGuid
        {
            get { return UnderlyingObject.Id; }
        }

        public override string ToString()
        {
            return UnderlyingObject.Reason;
        }
    }
}
