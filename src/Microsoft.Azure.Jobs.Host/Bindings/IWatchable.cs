namespace Microsoft.Azure.Jobs.Host.Bindings
{
    internal interface IWatchable
    {
        ISelfWatch Watcher { get; }
    }
}
