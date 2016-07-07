using System.Collections.Generic;
using System.Threading.Tasks;
using Fclp;

namespace WebJobs.Script.Cli.Interfaces
{
    internal interface IAction
    {
        ICommandLineParserResult ParseArgs(string[] args);
        Task RunAsync();
    }
}
