using System.Collections.Generic;
using System.Threading.Tasks;
using WebJobs.Script.Cli.Common;

namespace WebJobs.Script.Cli.Interfaces
{
    interface ITemplatesManager
    {
        Task<IEnumerable<Template>> Templates { get; }
        Task Deploy(string Name, Template template);
    }
}
