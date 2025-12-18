using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.Azure.Functions.WorkerModel.JobHost;
using Microsoft.Azure.WebJobs.Script.WebHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OutOfProcModel.Abstractions.Worker;
using OutOfProcModel.FunctionsHost.Grpc;

namespace OutOfProcModel.Mock;

internal interface IJobHostManager
{
    // gets or starts a new JobHost for this specific applicationId
    Task<JobHost> GetOrAddJobHostAsync(ApplicationDefinition appDefinition, Action<IServiceCollection> configureJobHost);

    Task<bool> TryGetJobHostAsync(ApplicationDefinition appDefinition, out JobHost jobHost);

    Task RemoveJobHostAsync(ApplicationDefinition appDefinition);

    // Sends a message to the appropriate JobHost
    Task HandleMessageAsync(MessageFromWorker message);
}

// mocking out to manage child containers
internal class JobHostManager(IScriptHostBuilderEx hostBuilder, ILogger<JobHostManager> logger) : IJobHostManager
{
    private readonly IScriptHostBuilderEx _builder = hostBuilder ?? throw new ArgumentNullException(nameof(hostBuilder));
    private readonly ILogger<JobHostManager> _logger = logger ?? throw new ArgumentNullException(nameof(logger));

    private readonly ConcurrentDictionary<ApplicationDefinition, JobHost> _jobHosts = new();

    public Task<JobHost> GetOrAddJobHostAsync(ApplicationDefinition appDefinition, Action<IServiceCollection> configureJobHost)
    {
        var jobHost = _jobHosts.GetOrAdd(appDefinition, _ =>
        {
            var host = _builder.BuildHost(false, false, services => configureJobHost?.Invoke(services));
            return new JobHost(host);
        });

        return Task.FromResult(jobHost);
    }

    public Task<bool> TryGetJobHostAsync(ApplicationDefinition appDefinition, out JobHost jobHost)
    {
        return Task.FromResult(_jobHosts.TryGetValue(appDefinition, out jobHost));
    }

    public async Task RemoveJobHostAsync(ApplicationDefinition appDefinition)
    {
        _jobHosts.Remove(appDefinition, out var jobHost);
        await jobHost!.StopAsync();
        jobHost.Dispose();
    }

    //public async Task AssignWorkerAsync(WorkerCreationContext context)
    //{
    //    if (await TryGetJobHostAsync(context.Definition, out var jobHost) && jobHost is not null)
    //    {
    //        await jobHost.WorkerManager.CreateWorkerAsync(context);
    //    }
    //}

    public async Task HandleMessageAsync(MessageFromWorker message)
    {
        if (await TryGetJobHostAsync(/* TODO: */ null, out var jobHost) && jobHost is not null)
        {
            await jobHost.Services.GetRequiredService<MessageHandlerPipeline>().HandleMessage(message);
        }
    }

    public async Task StopJobHostAsync(string applicationId)
    {
        // TODO:
        await Task.Yield();
        //if (_jobHosts.TryRemove(applicationId, out var host))
        //{
        //    await host.StopAsync();
        //    host.Dispose();
        //    _logger.LogInformation("Stopped JobHost for application {ApplicationId}", applicationId);
        //}
    }

    public string GetState()
    {
        List<AppState> jobHosts = [];

        foreach ((ApplicationDefinition appDef, JobHost jobHost) in _jobHosts)
        {
            var opt = jobHost.Services.GetRequiredService<IOptions<JobHostOptions>>().Value;

            var appState = new AppState(opt.ApplicationId, opt.ApplicationVersion);
            jobHosts.Add(appState);

            var workerManager = jobHost.Services.GetRequiredService<IWorkerManager>();
            var workers = workerManager.GetWorkers();
            foreach (var worker in workers)
            {
                appState.Workers.Add(new Worker { Id = worker.Definition.WorkerId, Status = worker.Status.ToString() });
            }
        }

        return JsonSerializer.Serialize(new { JobHosts = jobHosts }, new JsonSerializerOptions { WriteIndented = true });
    }

    private class Worker
    {
        public string Id { get; set; } = string.Empty;

        public string Status { get; set; } = string.Empty;
    }

    private class AppState(string appId, string appVersion)
    {
        public string ApplicationId { get; set; } = appId;

        public string ApplicationVersion { get; set; } = appVersion;

        public List<Worker> Workers { get; } = [];
    }
}