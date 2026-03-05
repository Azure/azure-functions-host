namespace Microsoft.Azure.Functions.WorkerModel.Workers;

//internal class WorkerModelFunctionMetadataProvider : IFunctionMetadataProvider
//{
//    private readonly IWorkerManager _workerManager;
//    private Lazy<Task<ImmutableArray<FunctionMetadata>>> _functionMetadataLazy;

//    public WorkerModelFunctionMetadataProvider(IWorkerManager workerManager)
//    {
//        _functionMetadataLazy = new Lazy<Task<ImmutableArray<FunctionMetadata>>>(LoadFunctionMetadataAsync);
//        _workerManager = workerManager;
//    }

//    public ImmutableDictionary<string, ImmutableArray<string>> FunctionErrors => throw new NotImplementedException();

//    public Task<ImmutableArray<FunctionMetadata>> GetFunctionMetadataAsync(IEnumerable<RpcWorkerConfig> workerConfigs, bool forceRefresh = false)
//    {
//        return _functionMetadataLazy.Value;
//    }

//    public async Task<ImmutableArray<FunctionMetadata>> LoadFunctionMetadataAsync()
//    {
//        await Task.Yield();

//        // TODO: Handle multiple workers scenario?
//        //       Or just ask for first always? They'll always be the same response.
//        IWorker worker = _workerManager.GetWorkers().First();
//        return await worker.GetFunctionMetadataAsync();
//    }
//}
