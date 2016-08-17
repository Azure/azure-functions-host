using System.Collections.Generic;
using Colors.Net;
using WebJobs.Script.Cli.Common.Models;

namespace WebJobs.Script.Cli.Interfaces
{
    internal interface ITipsManager
    {
        void Record(bool failed);
        IEnumerable<Invocation> GetInvocations(int count);
        IEnumerable<Invocation> GetAll();
        ITipsManager DisplayTip(string tip);
    }
}
