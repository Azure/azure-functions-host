using Microsoft.Azure.Functions.WorkerHarness.Grpc.Messages;

namespace WorkerHarness.Core
{
    public interface IMatch
    {
        bool Match(MatchingContext match, object source);

        bool MatchAll(IEnumerable<MatchingContext> matches, object source);
    }
}
