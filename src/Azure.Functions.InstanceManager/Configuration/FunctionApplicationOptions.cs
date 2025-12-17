namespace Microsoft.Azure.Functions.InstanceManager.Configuration;

internal sealed class FunctionApplicationOptions
{
    public string FunctionsWorkerRuntime { get; set; } = default!;

    public string ApplicationRoot { get; set; } = default!;
}