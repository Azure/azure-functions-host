using System.Threading.Tasks;

namespace WebJobs.Script.ConsoleHost.Commands
{
    public class ListCommand : BaseArmCommand
    {
        public override async Task Run()
        {
            foreach (var app in await _armManager.GetFunctionApps())
            {
                TraceInfo($"{app.SiteName} ({app.Location})");
            }
        }
    }
}
