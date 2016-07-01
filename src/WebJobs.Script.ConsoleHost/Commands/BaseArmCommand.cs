using CommandLine;
using WebJobs.Script.ConsoleHost.Arm;

namespace WebJobs.Script.ConsoleHost.Commands
{
    public abstract class BaseArmCommand : Command
    {
        public ArmManager _armManager { get; private set; }

        public BaseArmCommand()
        {
            _armManager = new ArmManager();
        }
    }
}
