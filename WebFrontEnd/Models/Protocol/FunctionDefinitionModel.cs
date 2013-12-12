using System;
using System.Linq;
using RunnerInterfaces;

namespace WebFrontEnd.Models.Protocol
{
    public class FunctionDefinitionModel
    {
        internal FunctionDefinition UnderlyingObject { get; private set; }

        internal FunctionDefinitionModel(FunctionDefinition underlyingObject)
        {
            UnderlyingObject = underlyingObject;
        }

        internal FunctionDefinitionModel(FunctionDefinition underlyingObject, RunningHost[] hosts)
            : this(underlyingObject)
        {
            HostIsRunning = HasValidHeartbeat(underlyingObject, hosts);
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

        public bool? HostIsRunning { get; private set; }

        public string RowKey { get { return UnderlyingObject.ToString(); } }

        private static bool HasValidHeartbeat(FunctionDefinition func, RunningHost[] heartbeats)
        {
            string assemblyFullName = func.GetAssemblyFullName();
            RunningHost heartbeat = heartbeats.FirstOrDefault(h => h.AssemblyFullName == assemblyFullName);
            return RunningHost.IsValidHeartbeat(heartbeat);
        }

        public override string ToString()
        {
            return UnderlyingObject.ToString();
        }
    }
}