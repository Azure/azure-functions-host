using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace Microsoft.Azure.Functions.WorkerModel.Configuration;

internal class FunctionApplicationOptionsSetup : IConfigureOptions<FunctionApplicationOptions>, IValidateOptions<FunctionApplicationOptions>
{
    private readonly IConfiguration _configuration;

    public FunctionApplicationOptionsSetup(IConfiguration configuration)
    {
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
    }

    public void Configure(FunctionApplicationOptions options)
    {
        options.ApplicationRoot = _configuration.GetValue<string>("AzureWebJobsScriptRoot") ?? string.Empty;
    }

    public ValidateOptionsResult Validate(string name, FunctionApplicationOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.ApplicationRoot))
        {
            return ValidateOptionsResult.Fail("AzureWebJobsScriptRoot must be set and cannot be empty.");
        }

        return ValidateOptionsResult.Success;
    }
}
