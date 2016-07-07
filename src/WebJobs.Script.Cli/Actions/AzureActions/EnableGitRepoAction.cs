using System;
using System.Threading.Tasks;
using WebJobs.Script.Cli.Arm;

namespace WebJobs.Script.Cli.Actions.AzureActions
{
    [Action(Name = "enable-git-repo", Context = Context.Azure, SubContext = Context.FunctionApp, HelpText = "Enable git repository on your Azure-hosted Function App")]
    class EnableGitRepoAction : BaseFunctionAppAction
    {
        private readonly IArmManager _armManager;

        public EnableGitRepoAction(IArmManager armManager)
        {
            _armManager = armManager;
        }

        public override async Task RunAsync()
        {
            var functionApp = await _armManager.GetFunctionAppAsync(FunctionAppName);
            await _armManager.EnsureScmTypeAsync(functionApp);
        }
    }
}
