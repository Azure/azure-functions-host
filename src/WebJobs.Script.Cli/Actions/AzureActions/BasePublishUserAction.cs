using System;
using System.Linq;
using Fclp;

namespace WebJobs.Script.Cli.Actions.AzureActions
{
    abstract class BasePublishUserAction : BaseAction
    {
        public string UserName { get; set; }

        public override ICommandLineParserResult ParseArgs(string[] args)
        {
            if (args.Any())
            {
                UserName = args.First();
            }
            else
            {
                throw new ArgumentException("Must specify a username.");
            }

            return base.ParseArgs(args);
        }
    }
}
