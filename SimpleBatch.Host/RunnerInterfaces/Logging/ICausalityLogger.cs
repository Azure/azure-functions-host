using System;
using System.Collections.Generic;
using Executor;

namespace RunnerInterfaces
{
    // Log function causality (eg, parent-child relationships). 
    // This is used to create a graph of parent-child causal relationships. 
    public interface ICausalityLogger
    {
        // Orchestrator (which is the thing that determines a function should get executed) calls this before Child is executed. 
        // ParentGuid is in reason 
        void LogTriggerReason(TriggerReason reason);
    }

    public interface ICausalityReader
    {
        // Given a function instance, get all the (immediate) children invoked because of this function. 
        //
        IEnumerable<TriggerReason> GetChildren(Guid parent);

        // Given a child, find the parent? It's in the FunctionInvokeRequest object. Easy. 
        // Expose it here too for consistency so a single interface can walk the causality graph.  
        Guid GetParent(Guid child);
    }

    // Provides a structured description for why a function was executed. 
    // Call ToString() for human readable string. 
    // Cast to a derived class to get more specific structured information.
    // These get serialized via JSON and stored in tables. 
    public abstract class TriggerReason
    {
        // The "current" guid that the rest of the trigger reason is valid for. 
        public Guid ChildGuid { get; set; }

        // Guid of parent function that triggered this one. 
        // Eg, if this func is triggered on [BlobInput], ParentGuid is the guid that wrote the blob. 
        // This is empty if there is no parent function (eg, a timer, or unknown blob writer).  
        public Guid ParentGuid { get; set; }

        // $$$ Also include Line number in parent function? Other diag info?
                
        public override string ToString()
        {
            return "Unknown reason";
        }
    }

    // This function was executed by a new blob was written. 
    // Corresponds to [BlobInput]
    public class BlobTriggerReason : TriggerReason
    {
        public CloudBlobPath BlobPath { get; set; }

        public override string ToString()
        {
            return "New blob input detected: " + BlobPath.ToString();
        }
    }

    // This function was executed via an ICall interface. 
    public class InvokeTriggerReason : TriggerReason
    {        
        public override string ToString()
        {
            return this.Message;
        }

        public string Message { get; set; }
    }

    // This function was executed because an AzureQueue Message
    // Corresponds to [QueueInput].
    public class QueueMessageTriggerReason : TriggerReason
    {
        public string MessageId { get; set; }

        public override string ToString()
        {
            return "New queue input message on queue";
        }
    }

    public class TimerTriggerReason : TriggerReason
    {
        public override string ToString()
        {
            return "Timer fired";
        }
    }
}