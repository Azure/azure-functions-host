namespace Microsoft.Azure.Jobs
{
    // Log as an individual function is getting updated. 
    // This may be called multiple times as a function execution is processed (queued, exectuing, completed, etc)
    // Called by whatever node "owns" the function (usually the executor).
    internal interface IFunctionUpdatedLogger
    {
        // The func can be partially filled out, and this will merge non-null fields onto the log. 

        // $$$ Beware, this encourages partially filled out ExecutionInstanceLogEntity to be floating around,
        //  which may cause confusion elsewhere (eg, wrong results from GetStatus). 
        //  Should this update func in place to be the latest results?
        void Log(ExecutionInstanceLogEntity func);
    }
}
