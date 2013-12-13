using System;
using RunnerInterfaces;

namespace WebFrontEnd.Models.Protocol
{
    public class FunctionTriggerModel
    {
        internal FunctionTrigger UnderlyingObject { get; private set; }

        internal FunctionTriggerModel(FunctionTrigger underlyingObject)
        {
            UnderlyingObject = underlyingObject;
        }

        public bool ListenOnBlobs
        {
            get { return UnderlyingObject.ListenOnBlobs; }
        }

        public TimeSpan? TimerInterval
        {
            get { return UnderlyingObject.TimerInterval; }
        }

        public override string ToString()
        {
            return UnderlyingObject.ToString();
        }
    }
}