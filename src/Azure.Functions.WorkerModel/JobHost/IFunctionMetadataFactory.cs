namespace OutOfProcModel.Abstractions.Worker;

public interface IFunctionMetadataFactory
{
    // mocked
    Task<IEnumerable<string>> GetFunctionMetadataAsync();
}
