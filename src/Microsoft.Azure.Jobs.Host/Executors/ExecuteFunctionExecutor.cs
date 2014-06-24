using Microsoft.Azure.Jobs.Host.Bindings;
using Microsoft.Azure.Jobs.Host.Runners;

namespace Microsoft.Azure.Jobs.Host.Executors
{
    internal class ExecuteFunctionExecutor : IFunctionExecutor
    {
        private readonly IExecuteFunction _executeFunction;
        private readonly RuntimeBindingProviderContext _context;

        public ExecuteFunctionExecutor(IExecuteFunction executeFunction, RuntimeBindingProviderContext context)
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
