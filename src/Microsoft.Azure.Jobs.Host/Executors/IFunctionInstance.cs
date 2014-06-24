using System;
using System.Reflection;
using Microsoft.Azure.Jobs.Host.Protocols;

namespace Microsoft.Azure.Jobs.Host.Executors
{
    internal interface IFunctionInstance
    {
        Guid Id { get; }

        Guid? ParentId { get; }

        ExecutionReason Reason { get; }

        IBindCommand BindCommand { get; }

        FunctionDescriptor FunctionDescriptor { get; }

        MethodInfo Method { get; }
    }
}
