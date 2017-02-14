using System.Collections.Generic;
using Microsoft.Azure.WebJobs.Script.Description;

namespace WebJobs.Script
{
    public interface IFunctionDescriptor
    {
        string Name { get; }
        FunctionMetadata Metadata { get; }

        IFunctionInvoker Invoker { get; }
    }
    public interface IScriptHost
    {
        ICollection<IFunctionDescriptor> Functions { get; }
    }
}
