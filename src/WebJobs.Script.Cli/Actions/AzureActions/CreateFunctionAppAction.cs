using System.Linq;
using System.Threading.Tasks;
using Colors.Net;
using Fclp;
using WebJobs.Script.Cli.Arm;
using static WebJobs.Script.Cli.Common.OutputTheme;

namespace WebJobs.Script.Cli.Actions.AzureActions
{
    [Action(Name = "create", Context = Context.Azure, SubContext = Context.FunctionApp, HelpText = "Create a new Function App in Azure with default settings")]
    internal class CreateFunctionAppAction : BaseAction
    {
        private IArmManager _armManager;
        public string ResourceGroup { get; set; }
        public string Subscription { get; set; }
        public string Name { get; set; }
        public string Location { get; set; }
        public string Plan { get; set; }

        public CreateFunctionAppAction(IArmManager armManager)
        {
            _armManager = armManager;
        }

        public override ICommandLineParserResult ParseArgs(string[] args)
        {
            var baseResult = base.ParseArgs(args);

            Parser
                .Setup<string>('g', "resource-group")
                .Callback(g => ResourceGroup = g)
                .Required();
            Parser
                .Setup<string>('n', "name")
                .Callback(n => Name = n)
                .Required();
            Parser
                .Setup<string>('s', "subscription")
                .Callback(s => Subscription = s)
                .Required();
            Parser
                .Setup<string>('p', "plan")
                .Callback(p => Plan = p)
                .Required();
            Parser
                .Setup<string>('l', "location")
                .Callback(l => Location = l)
                .Required();

            var result = Parser.Parse(args);
            var requiredOptions = Parser.Options.Count(o => o.IsRequired);

            if (result.UnMatchedOptions.Count() == requiredOptions && args.Length >= requiredOptions)
            {
                Subscription = args[0];
                ResourceGroup = args[1];
                Name = args[2];
                Location = args[3];
                Plan = args[4];
                return baseResult;
            }
            else
            {
                return result;
            }
        }

        public override async Task RunAsync()
        {
            var functionApp = await _armManager.CreateFunctionAppAsync(Subscription, ResourceGroup, Name, Location);
            ColoredConsole.WriteLine($"Function app {AdditionalInfoColor($"\"{functionApp.SiteName}\"")} has been created");
        }
    }
}
