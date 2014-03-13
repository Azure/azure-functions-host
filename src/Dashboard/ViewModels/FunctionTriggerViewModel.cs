using System;
using Microsoft.WindowsAzure.Jobs;

namespace Dashboard.ViewModels
{
    public class FunctionTriggerViewModel
    {
        internal FunctionTrigger UnderlyingObject { get; private set; }

        internal FunctionTriggerViewModel(FunctionTrigger underlyingObject)
        {
            UnderlyingObject = underlyingObject;
        }

        public bool ListenOnBlobs
        {
            get { return UnderlyingObject.ListenOnBlobs; }
        }

        public override string ToString()
        {
            return UnderlyingObject.ToString();
        }
    }
}
