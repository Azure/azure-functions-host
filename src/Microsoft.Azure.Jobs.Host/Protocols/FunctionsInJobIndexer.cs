using System;
using Microsoft.Azure.Jobs.Host.Storage.Table;

namespace Microsoft.Azure.Jobs.Host.Protocols
{
    /// <summary>
    /// Mapping WebJob runs to the functions that it ran.
    /// </summary>
    internal class FunctionsInJobIndexer : IFunctionsInJobIndexer
    {
        private readonly ICloudTable _table;
        private readonly WebJobRunIdentifier _currentWebJobRunId;

        private static readonly DateTime _nextCentury = DateTime.Parse("2100-01-01T00:00:00Z").ToUniversalTime();

        public FunctionsInJobIndexer(ICloudTableClient client, WebJobRunIdentifier currentWebJobRunId)
        {
            if (client == null)
            {
                throw new ArgumentNullException("client");
            }

            if (currentWebJobRunId == null)
            {
                throw new ArgumentNullException("currentWebJobRunId");
            }

            _table = client.GetTableReference(TableNames.FunctionsInJobIndex);
            _currentWebJobRunId = currentWebJobRunId;
        }

        public void RecordFunctionInvocationForJobRun(Guid invocationId, DateTime startTime)
        {
            var newEntity = new FunctionInvocationIndexEntity
            {
                PartitionKey = _currentWebJobRunId.GetKey(),
                RowKey = CreateRowKey(startTime),
                InvocationId = invocationId
            };
            _table.InsertEntity(newEntity);
        }

        // Provides a lexicographically sortable in descending order view of a given time
        // support queries of the form of "recent items"
        private static string CreateRowKey(DateTime time)
        {
            var secondsToNextCentury = (long)(_nextCentury - time.ToUniversalTime()).TotalSeconds;
            var entropy = Guid.NewGuid().ToString("N").Substring(20);
            return String.Format("{0:D10}${1}", secondsToNextCentury, entropy);
        }
    }
}