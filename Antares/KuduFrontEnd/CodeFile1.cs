using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Web.Http;
using Orchestrator;

namespace KuduFrontEnd
{
    // !!! Merge with Orch's IndexInMemory 
    class IndexInMemory : IFunctionTable
    {
        List<FunctionDefinition> List = new List<FunctionDefinition>();

        public void Add(FunctionDefinition func)
        {
            List.Add(func);
        }

        void IFunctionTable.Delete(FunctionDefinition func)
        {
            string funcString = func.ToString();
            foreach (var x in List)
            {
                if (x.ToString() == funcString)
                {
                    List.Remove(x);
                    return;
                }
            }
        }

        public FunctionDefinition Lookup(string functionId)
        {
            throw new NotImplementedException();
        }

        public FunctionDefinition[] ReadAll()
        {
            return List.ToArray();
        }
    }
}