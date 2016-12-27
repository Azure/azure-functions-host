using System;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;
using Colors.Net;
using WebJobs.Script.Cli.Arm;

namespace WebJobs.Script.Cli.Actions.AzureActions
{
    [Action(Name = "logstream", Context = Context.Azure, SubContext = Context.FunctionApp)]
    class LogStreamAction : BaseFunctionAppAction
    {
        private readonly IArmManager _armManager;

        public LogStreamAction(IArmManager armManager)
        {
            _armManager = armManager;
        }

        public override async Task RunAsync()
        {
            var functionApp = await _armManager.GetFunctionAppAsync(FunctionAppName);
            var basicHeaderValue = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{functionApp.PublishingUserName}:{functionApp.PublishingPassword}"));

            using (var client = new HttpClient())
            {
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", basicHeaderValue);
                var response = await client.GetStreamAsync(new Uri($"{functionApp.ScmUri}/api/logstream/application"));
                using (var reader = new StreamReader(response))
                {
                    var buffer = new char[4096];
                    var count = 0;
                    do
                    {
                        count = await reader.ReadAsync(buffer, 0, buffer.Length);
                        ColoredConsole.Write(new string(buffer.Take(count).ToArray()));
                    } while (count != 0);
                }
            }
        }
    }
}
