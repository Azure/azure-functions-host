using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Colors.Net;
using Microsoft.Azure.WebJobs.Script;
using WebJobs.Script.Cli.Common;
using static WebJobs.Script.Cli.Common.OutputTheme;

namespace WebJobs.Script.Cli.Actions.LocalActions
{
    [Action(Name = "init", HelpText = "Create a new Function App in the current folder. Initializes git repo.")]
    [Action(Name = "create", HelpText = "Create a new Function App in the current folder. Initializes git repo.")]
    [Action(Name = "init", Context = Context.FunctionApp, HelpText = "Create a new Function App in the current folder. Initializes git repo.")]
    [Action(Name = "create", Context = Context.FunctionApp, HelpText = "Create a new Function App in the current folder. Initializes git repo.")]
    class InitAction : BaseAction
    {
        public SourceControl SourceControl { get; set; } = SourceControl.Git;

        internal readonly Dictionary<string, string> fileToContentMap = new Dictionary<string, string>
        {
            { ".gitignore",  @"
bin
obj
csx
.vs
edge
Publish
.vscode

*.user
*.suo
*.cscfg
*.Cache
project.lock.json

/packages
/TestResults

/tools/NuGet.exe
/App_Data
/secrets
/data
.secrets
appsettings.json
"},
            { ScriptConstants.HostMetadataFileName, $"{{\"id\":\"{ Guid.NewGuid().ToString("N") }\"}}" },
            { SecretsManager.AppSettingsFileName, string.Empty }
        };

        public override async Task RunAsync()
        {
            if (SourceControl != SourceControl.Git)
            {
                throw new Exception("Only Git is supported right now for vsc");
            }

            foreach (var pair in fileToContentMap)
            {
                if (!FileSystemHelpers.FileExists(pair.Key))
                {
                    ColoredConsole.WriteLine($"Writing {pair.Key}");
                    await FileSystemHelpers.WriteAllTextToFileAsync(pair.Key, pair.Value);
                }
                else
                {
                    ColoredConsole.WriteLine($"{pair.Key} already exists. Skipped!");
                }
            }

            try
            {
                var checkGitRepoExe = new Executable("git", "rev-parse --git-dir");
                var result = await checkGitRepoExe.RunAsync();
                if (result != 0)
                {
                    var exe = new Executable("git", $"init");
                    await exe.RunAsync(l => ColoredConsole.WriteLine(l), l => ColoredConsole.Error.WriteLine(l));
                }
                else
                {
                    ColoredConsole.WriteLine("Directory already a git repository.");
                }
            }
            catch (FileNotFoundException)
            {
                ColoredConsole.WriteLine(WarningColor("unable to find git on the path"));
            }
        }
    }
}
