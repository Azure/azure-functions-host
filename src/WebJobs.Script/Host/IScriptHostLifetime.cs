using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Script.Description;

namespace Microsoft.Azure.WebJobs.Script.Host;

// TODO: (OOP - Refactor) - Review this
public interface IScriptHostLifetime
{
    Task InitializedAsync(IEnumerable<FunctionMetadata> functions, CancellationToken cancellationToken);
}
