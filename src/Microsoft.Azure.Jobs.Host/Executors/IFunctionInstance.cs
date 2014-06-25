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

        IBindingSource BindingSource { get; }

        FunctionDescriptor FunctionDescriptor { get; }

        MethodInfo Method { get; }
    }
}
