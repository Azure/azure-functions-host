using System.Collections.Generic;

namespace Dashboard.Data
{
    public interface IResultSegment<TResult>
    {
        IEnumerable<TResult> Results { get; }

        string ContinuationToken { get; }
    }
}
