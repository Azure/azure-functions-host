// !!! Give better name
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Executor;

namespace RunnerInterfaces
{
    // !!! Need to be race-condition free!
    public interface IPrereqManager
    {        
        // !!! Called from any node when queueing. 
        void AddPrereq(Guid func, IEnumerable<Guid> prereqs, IActivateFunction q);

        // Called by any node when the function executes. 
        // Called when Func completes. This notifies any other functions that have func as a prereq.
        // This may trigger other functions to execute.
        void OnComplete(Guid func, IActivateFunction q);

        // List the outstanding prereqs. 
        // This is crucially useful for answering "Why hasn't this function run yet?"
        IEnumerable<Guid> EnumeratePrereqs(Guid func);
    }
}