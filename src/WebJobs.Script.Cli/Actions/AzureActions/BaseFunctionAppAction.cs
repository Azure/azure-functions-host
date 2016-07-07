using System;
using System.Linq;
using Fclp;

namespace WebJobs.Script.Cli.Actions.AzureActions
{
    abstract class BaseFunctionAppAction : BaseAction
    {
        public string FunctionAppName { get; set; }

        public override ICommandLineParserResult ParseArgs(string[] args)
        {
            if (args.Any())
            {
                FunctionAppName = args.First();
            }
            else
            {
                throw new ArgumentException("Must specify functionApp name.");
            }

            return base.ParseArgs(args);
        }
    }
}
