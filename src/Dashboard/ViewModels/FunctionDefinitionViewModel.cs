using System;
using Microsoft.Azure.Jobs;

namespace Dashboard.ViewModels
{
    public class FunctionDefinitionViewModel
    {
        internal FunctionDefinition UnderlyingObject { get; private set; }

        internal FunctionDefinitionViewModel(FunctionDefinition underlyingObject)
        {
            UnderlyingObject = underlyingObject;
        }

        public string Description
        {
            get { return UnderlyingObject.Description; }
        }

        public DateTime Timestamp
        {
            get { return UnderlyingObject.Timestamp; }
        }

        public FunctionLocationViewModel Location
        {
            get { return new FunctionLocationViewModel(UnderlyingObject.Location); }
        }

        internal FunctionFlow Flow
        {
            get { return UnderlyingObject.Flow; }
        }

        public FunctionTriggerViewModel Trigger
        {
            get { return new FunctionTriggerViewModel(UnderlyingObject.Trigger); }
        }

        public string RowKey
        {
            get { return UnderlyingObject.ToString(); }
        }

        public override string ToString()
        {
            return UnderlyingObject.ToString();
        }
    }
}
