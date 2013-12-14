using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;


namespace Microsoft.WindowsAzure.Jobs
{
    // Interface for managed prerequisites in ICall
    internal interface IPrereqManager
    {        
        // Called from any node when queueing. 
        void AddPrereq(Guid func, IEnumerable<Guid> prereqs, IActivateFunction q);

        // Called by any node when the function executes. 
        // Called when Func completes. This notifies any other functions that have func as a prereq.
        // This may trigger other functions to execute.
        void OnComplete(Guid func, IActivateFunction q);

        // List the outstanding prereqs. 
        // This is crucially useful for answering "Why hasn't this function run yet?"
        // List should be empty after the function is queued (since there are no longer outstanding prereqs)
        IEnumerable<Guid> EnumeratePrereqs(Guid func);
    }
}