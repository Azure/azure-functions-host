using System.Collections.Generic;

namespace Microsoft.Azure.Jobs.Internals
{
    // $$$ Merge with Orch's IndexInMemory 
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
            // $$$ Not linear :(
            foreach (var x in List)
            {
                if (x.Location.ToString() == functionId)
                {
                    return x;
                }
            }
            return null;
        }

        public FunctionDefinition[] ReadAll()
        {
            return List.ToArray();
        }
    }
}
