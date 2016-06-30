using CommandLine;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace WebJobs.Script.ConsoleHost.Cli
{
    public class PullVerbOptions : BaseAbstractOptions
    {
        [ValueOption(0)]
        public string FunctionAppName { get; set; }
    }
}
