using System.Linq;
using RunnerInterfaces;
using WebFrontEnd.Models.Protocol;

namespace WebFrontEnd.Controllers
{
    public class RunningFunctionDefinitionModel
    {
        public RunningFunctionDefinitionModel(FunctionDefinitionModel functionDefinition, RunningHost[] heartbeats)
        {
            HostIsRunning = HasValidHeartbeat(functionDefinition.UnderlyingObject, heartbeats);
            FunctionDefinition = functionDefinition;
        }

        public FunctionDefinitionModel FunctionDefinition { get; private set; }
        public bool HostIsRunning { get; private set; }

        private static bool HasValidHeartbeat(FunctionDefinition func, RunningHost[] heartbeats)
        {
            string assemblyFullName = func.GetAssemblyFullName();
            RunningHost heartbeat = heartbeats.FirstOrDefault(h => h.AssemblyFullName == assemblyFullName);
            return RunningHost.IsValidHeartbeat(heartbeat);
        }
    }
}