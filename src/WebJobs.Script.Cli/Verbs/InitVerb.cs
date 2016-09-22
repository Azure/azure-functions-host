// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Colors.Net;
using Microsoft.Azure.WebJobs.Script;
using NCli;
using WebJobs.Script.Cli.Common;
using WebJobs.Script.Cli.Interfaces;
using static WebJobs.Script.Cli.Common.OutputTheme;

namespace WebJobs.Script.Cli.Verbs
{
    [Verb(HelpText = "Creates .gitignore, and host.json. Runs git init .")]
    internal class InitVerb : BaseVerb
    {
        [Option(0)]
        public string Folder { get; set; }

        [Option("vsc", DefaultValue = SourceControl.Git, HelpText = "Version Control Software. Git or Hg")]
        public SourceControl SourceControl { get; set; }

        internal readonly Dictionary<string, string> fileToContentMap = new Dictionary<string, string>
        {
            { ".gitignore",  @"
bin
obj
csx
.vs
edge
Publish

*.user
*.suo
*.cscfg
*.Cache

/packages
/TestResults

/tools/NuGet.exe
/App_Data
/secrets
/data
.secrets
"},
            { ScriptConstants.HostMetadataFileName, $"{{\"id\":\"{ Guid.NewGuid().ToString("N") }\"}}" },
            { SecretsManager.SecretsFilePath, string.Empty }
        };

        public InitVerb(ITipsManager tipsManager)
            : base(tipsManager)
        {
        }

        public override async Task RunAsync()
        {
            if (SourceControl != SourceControl.Git)
            {
                throw new Exception("Only Git is supported right now for vsc");
            }

            foreach (var pair in fileToContentMap)
            {
                ColoredConsole.WriteLine($"Writing {pair.Key}");
                await FileSystemHelpers.WriteAllTextToFileAsync(pair.Key, pair.Value);
            }

            var exe = new Executable("git", "init");
            await exe.RunAsync(l => ColoredConsole.WriteLine(l), l => ColoredConsole.Error.WriteLine(l));

            _tipsManager.DisplayTips($"{TitleColor("Tip:")} run {ExampleColor("func new")} to create your fSirst function.");
        }
    }
}
