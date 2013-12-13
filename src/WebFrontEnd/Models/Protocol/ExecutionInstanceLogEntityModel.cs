using System;
using Executor;

namespace WebFrontEnd.Models.Protocol
{
    public class ExecutionInstanceLogEntityModel
    {
        internal ExecutionInstanceLogEntity UnderlyingObject { get; private set; }

        internal ExecutionInstanceLogEntityModel(ExecutionInstanceLogEntity underlyingObject)
        {
            UnderlyingObject = underlyingObject;
            Name = underlyingObject.ToString();
            FunctionInstance = new FunctionInvokeRequestModel(underlyingObject.FunctionInstance);
        }

        public FunctionInvokeRequestModel FunctionInstance { get; private set; }

        public string Name { get; private set; }

        public DateTime Timestamp
        {
            get { return UnderlyingObject.Timestamp; }
        }

        public string OutputUrl
        {
            get { return UnderlyingObject.OutputUrl; }
        }

        public FunctionInstanceStatusModel GetStatus()
        {
            return (FunctionInstanceStatusModel)UnderlyingObject.GetStatus();
        }

        public string GetRunStatusString()
        {
            return UnderlyingObject.GetRunStatusString();
        }

        public string GetKey()
        {
            return UnderlyingObject.GetKey();
        }

        public override string ToString()
        {
            return UnderlyingObject.ToString();
        }
    }
}