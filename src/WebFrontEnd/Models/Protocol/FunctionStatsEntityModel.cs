using System;

namespace Microsoft.WindowsAzure.Jobs.Dashboard.Models.Protocol
{
    public class FunctionStatsEntityModel
    {
        internal FunctionStatsEntity UnderlyingObject { get; private set; }

        internal FunctionStatsEntityModel(FunctionStatsEntity underlyingObject)
        {
            UnderlyingObject = underlyingObject;
        }

        public int CountCompleted
        {
            get { return UnderlyingObject.CountCompleted; }
        }

        public TimeSpan Runtime
        {
            get { return UnderlyingObject.Runtime; }
        }

        public int CountErrors
        {
            get { return UnderlyingObject.CountErrors; }
        }

        public override string ToString()
        {
            return UnderlyingObject.ToString();
        }
    }
}