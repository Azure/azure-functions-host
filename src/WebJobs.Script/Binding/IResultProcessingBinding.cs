using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Microsoft.Azure.WebJobs.Script.Binding
{
    /// <summary>
    /// Represents a binding that may process function execution results.
    /// </summary>
    public interface IResultProcessingBinding
    {
        void ProcessResult(object inputValue, object result);

        bool CanProcessResult(object result);
    }
}
