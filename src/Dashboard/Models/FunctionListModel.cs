using System.Collections.Generic;
using System.Linq;

namespace Dashboard.Controllers
{
    public class FunctionListModel
    {
        public IEnumerable<IGrouping<object, RunningFunctionDefinitionModel>> Functions { get; set; }

        public bool HasWarning { get; set; }
    }
}
