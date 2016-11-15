using System;
using System.Threading.Tasks;
using Colors.Net;
using WebJobs.Script.Cli.Arm;
using WebJobs.Script.Cli.Interfaces;
using static WebJobs.Script.Cli.Common.OutputTheme;

namespace WebJobs.Script.Cli.Actions.AzureActions
{
    [Action(Name = "fetch-app-settings", Context = Context.Azure, SubContext = Context.FunctionApp, HelpText = "Retrieve App Settings from your Azure-hosted Function App and store locally")]
    [Action(Name = "fetch", Context = Context.Azure, SubContext = Context.FunctionApp, HelpText = "Retrieve App Settings from your Azure-hosted Function App and store locally")]
    class FetchAppSettingsAction : BaseFunctionAppAction
    {
        private readonly IArmManager _armManager;
        private ISecretsManager _secretsManager;

        public FetchAppSettingsAction(IArmManager armManager, ISecretsManager secretsManager)
        {
            _armManager = armManager;
            _secretsManager = secretsManager;
        }

        public override async Task RunAsync()
        {
            var functionApp = await _armManager.GetFunctionAppAsync(FunctionAppName);
            if (functionApp != null)
            {
                var secrets = await _armManager.GetFunctionAppAppSettings(functionApp);
                foreach (var pair in secrets)
                {
                    ColoredConsole.WriteLine($"Loading {pair.Key} = *****");
                    _secretsManager.SetSecret(pair.Key, pair.Value);
                }
            }
            else
            {
                ColoredConsole.Error.WriteLine(ErrorColor($"Can't find function app by name {FunctionAppName}"));
            }
        }
    }
}
