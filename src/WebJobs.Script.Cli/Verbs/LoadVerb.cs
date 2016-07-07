using System.Threading.Tasks;
using Colors.Net;
using NCli;
using WebJobs.Script.Cli.Arm;
using WebJobs.Script.Cli.Common;
using WebJobs.Script.Cli.Interfaces;
using static WebJobs.Script.Cli.Common.OutputTheme;

namespace WebJobs.Script.Cli.Verbs
{
    internal class LoadVerb : BaseVerb
    {

        [Option(0)]
        public Loadable? ToLoad { get; set; }

        [Option(1)]
        public string FunctionAppName { get; set; }

        private readonly IArmManager _armManager;
        private readonly ISecretsManager _secretsManager;

        public LoadVerb(IArmManager armManager, ISecretsManager secretsManager)
        {
            _armManager = armManager;
            _secretsManager = secretsManager;
        }

        public override async Task RunAsync()
        {
            if (ToLoad == null)
            {
                ColoredConsole
                    .Error
                    .WriteLine("Must specify what to load.")
                    .WriteLine($"Currently only {ExampleColor("func load")} secrets works");
            }
            else if (ToLoad == Loadable.Secrets)
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
                    ColoredConsole.Error.WriteLine(ErrorColor("Can't find function app by name {}"));
                }
            }
        }
    }
}
