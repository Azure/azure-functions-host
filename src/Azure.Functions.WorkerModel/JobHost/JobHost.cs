using Microsoft.Azure.WebJobs.Script;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using OutOfProcModel.Abstractions.Worker;

namespace OutOfProcModel.Mock;

internal class JobHost : IHost
{
    private readonly IHost _host;
    private bool _isStarted = false;

    public JobHost(IHost host)
    {
        _host = host ?? throw new ArgumentNullException(nameof(host));
    }

    public IServiceProvider Services => _host.Services;

    public IFunctionMetadataProvider MetadataProvider => _host.Services.GetRequiredService<IFunctionMetadataProvider>();

    public async Task<IWorkerState> CreateWorkerAsync(WorkerCreationContext workerCreationContext)
    {
        var workerManager = _host.Services.GetRequiredService<IWorkerManager>();
        return await workerManager.CreateWorkerAsync(workerCreationContext);
    }

    public void Dispose()
    {
        _host.Dispose();
    }

    public Task StartAsync(CancellationToken cancellationToken = default)
    {
        if (!_isStarted)
        {
            _isStarted = true;
            return _host.StartAsync(cancellationToken);
        }

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken = default)
    {
        return _host.StopAsync(cancellationToken);
    }

    public Task DrainAsync(CancellationToken cancellationToken = default)
    {
        // todo: stop listeners before returning

        List<Task> tasks = [];
        foreach (var worker in _host.Services.GetRequiredService<IWorkerManager>().GetWorkers())
        {
            tasks.Add(worker.DrainAsync(TimeSpan.FromSeconds(30)));
        }

        return Task.WhenAll(tasks);
    }
}
