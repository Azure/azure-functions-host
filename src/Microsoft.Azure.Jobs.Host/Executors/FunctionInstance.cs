using System;
using System.Reflection;
using Microsoft.Azure.Jobs.Host.Protocols;

namespace Microsoft.Azure.Jobs.Host.Executors
{
    internal class FunctionInstance : IFunctionInstance
    {
        private readonly Guid _id;
        private readonly Guid? _parentId;
        private readonly ExecutionReason _reason;
        private readonly IBindingSource _bindingSource;
        private readonly FunctionDescriptor _functionDescriptor;
        private readonly MethodInfo _method;

        public FunctionInstance(Guid id, Guid? parentId, ExecutionReason reason, IBindingSource bindingSource,
            FunctionDescriptor functionDescriptor, MethodInfo method)
        {
            _id = id;
            _parentId = parentId;
            _reason = reason;
            _bindingSource = bindingSource;
            _functionDescriptor = functionDescriptor;
            _method = method;
        }

        public Guid Id
        {
            get { return _id; }
        }

        public Guid? ParentId
        {
            get { return _parentId; }
        }

        public ExecutionReason Reason
        {
            get { return _reason; }
        }

        public IBindingSource BindingSource
        {
            get { return _bindingSource; }
        }

        public FunctionDescriptor FunctionDescriptor
        {
            get { return _functionDescriptor; }
        }

        public MethodInfo Method
        {
            get { return _method; }
        }
    }
}
