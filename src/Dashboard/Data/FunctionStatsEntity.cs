using System;

namespace Dashboard.Data
{
    // Statistics per function type, aggregated across all instances.
    // These must all be monotonically increasing numbers, since they're continually aggregated.
    // Eg, we can't do queud. 
    internal class FunctionStatsEntity
    {
        public DateTime LastWriteTime { get; set; } // last time function was executed and succeeded

        public int CountCompleted { get; set; } // Total run            
        public int CountErrors { get; set; } // number of runs with failure status
        public TimeSpan Runtime { get; set; } // total time spent running.         
    }
}
