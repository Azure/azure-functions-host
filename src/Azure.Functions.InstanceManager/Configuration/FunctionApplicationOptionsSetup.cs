using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace Microsoft.Azure.Functions.InstanceManager.Configuration;

internal class FunctionApplicationOptionsSetup : IConfigureOptions<FunctionApplicationOptions>, IValidateOptions<FunctionApplicationOptions>
{
    private readonly IConfiguration _configuration;

    public FunctionApplicationOptionsSetup(IConfiguration configuration)
    {
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
    }

    public void Configure(FunctionApplicationOptions options)
    {
        options.FunctionsWorkerRuntime = _configuration.GetValue<string>("FUNCTIONS_WORKER_RUNTIME") ?? string.Empty;
        options.ApplicationRoot = _configuration.GetValue<string>("AzureWebJobsScriptRoot") ?? string.Empty;
    }

    public ValidateOptionsResult Validate(string? name, FunctionApplicationOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.FunctionsWorkerRuntime))
        {
            return ValidateOptionsResult.Fail("FUNCTIONS_WORKER_RUNTIME must be set and cannot be empty.");
        }

        if (string.IsNullOrWhiteSpace(options.ApplicationRoot))
        {
            return ValidateOptionsResult.Fail("AzureWebJobsScriptRoot must be set and cannot be empty.");
        }

        return ValidateOptionsResult.Success;
    }
}
