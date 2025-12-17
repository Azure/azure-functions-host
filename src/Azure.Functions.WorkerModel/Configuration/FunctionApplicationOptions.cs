namespace Microsoft.Azure.Functions.WorkerModel.Configuration;

internal sealed class FunctionApplicationOptions
{
    public string ApplicationRoot { get; set; } = default!;

    public string DefaultApplicationId { get; set; } = "_Application";

    public string DefaultApplicationVersion { get; set; } = "_Version";
}