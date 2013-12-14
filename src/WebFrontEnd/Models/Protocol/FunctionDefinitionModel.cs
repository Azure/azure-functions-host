using System;

namespace Microsoft.WindowsAzure.Jobs.Dashboard.Models.Protocol
{
    public class FunctionDefinitionModel
    {
        internal FunctionDefinition UnderlyingObject { get; private set; }

        internal FunctionDefinitionModel(FunctionDefinition underlyingObject)
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

        public FunctionLocationModel Location
        {
            get { return new FunctionLocationModel(UnderlyingObject.Location); }
        }

        internal FunctionFlow Flow
        {
            get { return UnderlyingObject.Flow; }
        }

        public FunctionTriggerModel Trigger
        {
            get { return new FunctionTriggerModel(UnderlyingObject.Trigger); }
        }

        public string RowKey { get { return UnderlyingObject.ToString(); } }

        public override string ToString()
        {
            return UnderlyingObject.ToString();
        }
    }
}