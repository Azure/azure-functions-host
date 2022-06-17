using Microsoft.Azure.Functions.WorkerHarness.Grpc.Messages;

namespace WorkerHarness.Core
{
    public interface IActionWriter
    {
        IList<MatchingCriteria> Match { get; }

        IDictionary<ValidationContext, bool> ValidationResults { get; }

        void WriteActionName(string name);

        void WriteSentMessage(StreamingMessage message);

        void WriteMatchedMessage(StreamingMessage message);

        void WriteUnmatchedMessages(IncomingMessage message);

        void WriteActionEnding();
    }
}