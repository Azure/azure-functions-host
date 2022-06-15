using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace WorkerHarness.Core
{
    internal interface IVariableManager
    {
        void Subscribe(Expression expression);

        void AddVariable(string variableName, object variableValue);
    }
}
