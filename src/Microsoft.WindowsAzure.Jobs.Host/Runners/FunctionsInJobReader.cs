using System;
using System.Linq;
using Microsoft.WindowsAzure.Jobs.Host.Storage.Table;

namespace Microsoft.WindowsAzure.Jobs
{
    internal class FunctionsInJobReader : IFunctionsInJobReader
    {
        private readonly ICloudTable _table;

        public FunctionsInJobReader(ICloudTableClient client)
        {
            if (client == null)
            {
                throw new ArgumentNullException("client");
            }

            _table = client.GetTableReference(TableNames.FunctionsInJobIndex);
        }

        // We need to support a few patterns of queries here:
        // 1. initial page load (most recent 20 invocations), before and after would be null
        // 2. further pages of older invocations - before would be the oldest known rowkey
        // 3. new invocations since page load (in the page's polling mechanism) - after would be the most recent known rowkey
        public FunctionInJobEntity[] GetFunctionInvocationsForJobRun(WebJobRunIdentifier jobId, string olderThan, string newerThan, int? limit)
        {
            // the rowkey always starts with digits, to "0" is always smaller, and "A" is always larger
            var upperBound = newerThan ?? "A";
            var lowerBound = olderThan ?? "0";

            var stuff = _table.QueryByRowKeyRange<FunctionInJobEntity>(
                jobId.GetKey(), lowerBound, upperBound, limit);

            return stuff.ToArray();
        }
    }
}