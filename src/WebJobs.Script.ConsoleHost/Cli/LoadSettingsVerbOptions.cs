using CommandLine;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using WebJobs.Script.ConsoleHost.Cli.Types;

namespace WebJobs.Script.ConsoleHost.Cli
{
    [VerbName("load-settings")]
    public class LoadSettingsVerbOptions : BaseAbstractOptions
    {
        [ValueOption(0)]
        public string FunctionAppName { get; set; }
    }
}
