using Microsoft.Azure.WebJobs.Script;

namespace Microsoft.Extensions.DependencyInjection
{
    internal class WorkerModelScriptHostManager : IScriptHostManager
    {
        public event EventHandler HostInitializing;

        public event EventHandler<ActiveHostChangedEventArgs> ActiveHostChanged;

        public ScriptHostState State { get; private set; } = ScriptHostState.Default;

        public Exception LastError => throw new NotImplementedException();

        public IServiceProvider Services => null;

        public Task RestartHostAsync(string reason, CancellationToken cancellationToken = default)
        {
            State = ScriptHostState.Running;
            return Task.CompletedTask;
        }
    }
}