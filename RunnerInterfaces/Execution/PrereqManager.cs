using System;
using System.Collections.Generic;
using System.Linq;
using Executor;
using SimpleBatch;

namespace RunnerInterfaces
{
    // !!! Really scrutinize for race conditions and hammer stress test this.
    public class PrereqManager : IPrereqManager
    {
        // PartKey, RowKey = PreReqGuid, target Guid. 
        // This is used as a secondary index into the prereq table
        private readonly IAzureTable _successorTable;

        // Part,Row = TargetGuid, PreReq
        // This can enumeraet a given function's prerequisites.
        private readonly IAzureTable _prereqTable;

        // !!! Do we need full status? Or just Success/Fail/NotYetDone
        Func<Guid, FunctionInstanceStatus> _fpGetStatus;


        public PrereqManager(IAzureTable prereqTable, IAzureTable successorTable, IFunctionInstanceLookup lookup)
            : this(prereqTable, successorTable, guid => GetFunctionStatus(lookup, guid))
        {
        }

        public PrereqManager(IAzureTable prereqTable, IAzureTable successorTable, Func<Guid, FunctionInstanceStatus> fpIsDone)
        {
            _prereqTable = prereqTable;
            _successorTable = successorTable;

            _fpGetStatus = fpIsDone;
        }

        public void AddPrereq(Guid func, IEnumerable<Guid> prereqs, IActivateFunction q)
        {
            int count = 0;
            var empty = new { };
            foreach (Guid prereq in prereqs)
            {
                if (!IsDone(prereq))
                {
                    _successorTable.Write(prereq.ToString(), func.ToString(), empty);
                    _prereqTable.Write(func.ToString(), prereq.ToString(), empty);
                    count++;
                }
            }
            _successorTable.Flush();
            _prereqTable.Flush();

            if (count == 0)
            {
                Activate(q, func);
            }
        }

        private bool IsDone(Guid func)
        {
            var status = this._fpGetStatus(func);
            return status == FunctionInstanceStatus.CompletedSuccess; // !!! failure?
        }

        private void Activate(IActivateFunction q, Guid func)
        {
            q.ActivateFunction(func);
        }

        // !!! Beware, this could be called multiple times for the same function. 
        public void OnComplete(Guid func, IActivateFunction q)
        {
            // !!! Do we care if IsDone should be set true yet? Caller may have not yet set.

            // For all prereqs = func, decrement the count
            foreach (Guid child in EnumerateSuccessors(func))
            {
                _prereqTable.Delete(child.ToString(), func.ToString());

                if (IsReady(child))
                {
                    Activate(q, child);
                }
            }

            // Function has finished executing, so we can delete all related entries.
            string partKey = func.ToString();
            _successorTable.Delete(partKey);            
        }

        // Is the given function already completed?
        private static FunctionInstanceStatus GetFunctionStatus(IFunctionInstanceLookup lookup, Guid func)
        {
            // !!! What about if a prereq completes with an error?            
            var log = lookup.Lookup(func);
            return log.GetStatus();
        }

        // Are all the prereqs for a given function satisfied?
        public bool IsReady(Guid func)
        {
            var list = EnumeratePrereqs(func);
            bool hasOutstandingPrereqs = list.Any();
            return !hasOutstandingPrereqs;
        }

        public IEnumerable<Guid> EnumeratePrereqs(Guid func)
        {
            var list = _prereqTable.Enumerate(func.ToString());
            return from dict in list select Guid.Parse(dict["RowKey"]);
        }

        public IEnumerable<Guid> EnumerateSuccessors(Guid func)
        {
            string partKey = func.ToString();
            return from dict in _successorTable.Enumerate(partKey) select Guid.Parse(dict["RowKey"]);
        }
    }
};