using System;
using Microsoft.Azure.Jobs;
using Microsoft.Azure.Jobs.Host.Protocols;
using Microsoft.Azure.Jobs.Host.Storage.Table;

namespace Dashboard.Data
{
    /// <summary>
    /// Mapping WebJob runs to the functions that it ran.
    /// </summary>
    internal class FunctionsInJobIndexer : IFunctionsInJobIndexer
    {
        private readonly ICloudTable _table;

        private static readonly DateTime _nextCentury = DateTime.Parse("2100-01-01T00:00:00Z").ToUniversalTime();

        public FunctionsInJobIndexer(ICloudTableClient client)
        {
            if (client == null)
            {
                throw new ArgumentNullException("client");
            }

            _table = client.GetTableReference(DashboardTableNames.FunctionsInJobIndex);
        }

        public void RecordFunctionInvocationForJobRun(Guid invocationId, DateTime startTime, WebJobRunIdentifier webJobRunId)
        {
            if (webJobRunId == null)
            {
                throw new ArgumentNullException("webJobRunId");
            }

            var newEntity = new FunctionInvocationIndexEntity
            {
                PartitionKey = webJobRunId.GetKey(),
                RowKey = CreateRowKey(startTime),
                InvocationId = invocationId
            };
            _table.Insert(newEntity);
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