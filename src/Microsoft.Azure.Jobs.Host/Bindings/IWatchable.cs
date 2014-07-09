namespace Microsoft.Azure.Jobs.Host.Bindings
{
    internal interface IWatchable
    {
        IWatcher Watcher { get; }
    }
}
