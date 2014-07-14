using Microsoft.Azure.Jobs.Host.Bindings;

namespace Microsoft.Azure.Jobs.Host.Executors
{
    internal class ExecuteFunctionExecutor : IFunctionExecutor
    {
        private readonly IExecuteFunction _executeFunction;
        private readonly HostBindingContext _context;

        public ExecuteFunctionExecutor(IExecuteFunction executeFunction, HostBindingContext context)
        {
            _executeFunction = executeFunction;
            _context = context;
        }

        public bool Execute(IFunctionInstance instance)
        {
            FunctionInvocationResult result = _executeFunction.Execute(instance, _context);

            return result.Succeeded;
        }
    }
}
