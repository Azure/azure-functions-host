using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;

namespace Microsoft.Azure.WebJobs.Script.Abstractions.Rpc
{
    public interface IRpcServer
    {
        void Start();

        Task ShutdownAsync();

        Uri Uri { get; }
    }
}
