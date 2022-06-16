using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace WorkerHarness.Core
{
    public interface IVariableManager
    {
        void Subscribe(Expression expression);

        void AddVariable(string variableName, object variableValue);

        void Clear();
    }
}
