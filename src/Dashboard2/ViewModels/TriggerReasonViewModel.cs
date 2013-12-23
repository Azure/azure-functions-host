using System;
using Microsoft.WindowsAzure.Jobs;

namespace Dashboard.ViewModels
{
    public class TriggerReasonViewModel
    {
        internal TriggerReason UnderlyingObject { get; private set; }

        internal TriggerReasonViewModel(TriggerReason underlyingObject)
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