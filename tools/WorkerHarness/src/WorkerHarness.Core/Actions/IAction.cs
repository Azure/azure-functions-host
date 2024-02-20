namespace WorkerHarness.Core.Actions
{
    /// <summary>
    /// An abstraction for an action in a scenario file
    /// </summary>
    public interface IAction
    {
        Task<ActionResult> ExecuteAsync(ExecutionContext executionContext);
    }
}
