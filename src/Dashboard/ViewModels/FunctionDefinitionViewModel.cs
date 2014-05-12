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

        public FunctionLocationViewModel Location
        {
            get { return new FunctionLocationViewModel(UnderlyingObject.Location); }
        }

        internal FunctionFlow Flow
        {
            get { return UnderlyingObject.Flow; }
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
