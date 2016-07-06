using CommandLine;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using WebJobs.Script.ConsoleHost.Arm.Models;
using WebJobs.Script.ConsoleHost.Common;

namespace WebJobs.Script.ConsoleHost.Commands
{
    [CommandNames("pull", "clone", "push", "fetch")]
    public class GitCommand : FunctionAppBaseCommand
    {
        [ValueList(typeof(List<string>))]
        public IList<string> GitCommandLine { get; set; }

        public override async Task Run()
        {
            var user = await _armManager.GetUser();
            var functionApp = await _armManager.GetFunctionApp(FunctionAppName);
            if (functionApp != null)
            {
                await _armManager.EnsureScmType(functionApp);
                await _armManager.LoadSitePublishingCredentials(functionApp);
                await RunGit(OriginalCommand, functionApp);
            }
            else
            {
                FunctionAppNotFound();
            }
        }

        private async Task RunGit(string command, Site functionApp)
        {
            var commandLine = GitCommandLine?.Aggregate(string.Empty, (a, b) => $"{a} {b}") ?? string.Empty;
            var git = new Executable("git.exe", $"{command} \"{functionApp.ScmUri}\" {commandLine}");
            await git.RunAsync(TraceInfo, TraceInfo);
        }
    }
}
