using System;
using System.Collections.Generic;
using Microsoft.Azure.Jobs;

namespace Dashboard.Data
{
    internal interface ICausalityReader
    {
        // Given a function instance, get all the (immediate) children invoked because of this function. 
        //
        IEnumerable<TriggerReason> GetChildren(Guid parent);
        
        IEnumerable<TriggerReasonEntity> GetChildren(Guid parent, string rowKeyExclusiveUpperBound, string rowKeyExclusiveLowerBound, int limit);
        // Given a child, find the parent? It's in the FunctionInvokeRequest object. Easy. 
        // Expose it here too for consistency so a single interface can walk the causality graph.  
        Guid GetParent(Guid child);
    }
}
