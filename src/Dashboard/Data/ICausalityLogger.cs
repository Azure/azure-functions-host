using Microsoft.Azure.Jobs;

namespace Dashboard.Data
{
    // Log function causality (eg, parent-child relationships). 
    // This is used to create a graph of parent-child causal relationships. 
    internal interface ICausalityLogger
    {
        // Orchestrator (which is the thing that determines a function should get executed) calls this before Child is executed. 
        // ParentGuid is in reason 
        void LogTriggerReason(TriggerReason reason);
    }
}
