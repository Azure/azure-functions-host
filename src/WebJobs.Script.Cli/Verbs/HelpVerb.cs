// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Threading.Tasks;
using Colors.Net;
using NCli;
using WebJobs.Script.Cli.Interfaces;
using static WebJobs.Script.Cli.Common.OutputTheme;

namespace WebJobs.Script.Cli.Verbs
{
    [Verb(ShowInHelp = false)]
    internal class HelpVerb : BaseVerb
    {
        private readonly HelpTextCollection _help;

        [Option(0)]
        public string Verb { get; set; }

        public HelpVerb(HelpTextCollection help, ITipsManager tipsManager)
            : base(tipsManager)
        {
            _help = help;
        }

        public override Task RunAsync()
        {
            ColoredConsole.WriteLine("Azure Functions CLI 0.1");
            _help.ForEach(l => ColoredConsole.WriteLine(l.ToString()));

            _tipsManager.DisplayTip($"{TitleColor("Tip:")} run {ExampleColor("func init")} to get started.");

            return Task.CompletedTask;
        }
    }
}
