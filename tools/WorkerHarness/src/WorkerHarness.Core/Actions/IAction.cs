namespace WorkerHarness.Core
{
    /// <summary>
    /// An abtraction for an action in a scenario file
    /// </summary>
    public interface IAction
    {
        Task<ActionResult> ExecuteAsync(ExecutionContext execuationContext);
    }
}
