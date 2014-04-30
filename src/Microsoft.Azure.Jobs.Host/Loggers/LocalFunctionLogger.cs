using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using Microsoft.Azure.Jobs.Internals;
using Microsoft.WindowsAzure.Storage;
using AzureTables;

namespace Microsoft.Azure.Jobs
{
    // Ideally use FunctionUpdatedLogger with in-memory azure tables (this would minimize code deltas).
    // For local execution, we may have function objects that don't serialize. So we can't run through azure tables.
    // $$$ Better way to merge?
    internal class LocalFunctionLogger : IFunctionUpdatedLogger, IFunctionInstanceLookup
    {
        Dictionary<string, ExecutionInstanceLogEntity> _dict = new Dictionary<string, ExecutionInstanceLogEntity>();

        void IFunctionUpdatedLogger.Log(ExecutionInstanceLogEntity log)
        {
            string rowKey = log.GetKey();

            var l2 = this.Lookup(rowKey);
            if (l2 == null)
            {
                l2 = log;
            }
            else
            {
                // Merge
                FunctionUpdatedLogger.Merge(l2, log);
            }

            _dict[rowKey] = l2;
        }

        ExecutionInstanceLogEntity IFunctionInstanceLookup.Lookup(Guid rowKey)
        {
            return Lookup(rowKey.ToString());
        }

        private ExecutionInstanceLogEntity Lookup(string rowKey)
        {
            ExecutionInstanceLogEntity log;
            _dict.TryGetValue(rowKey, out log);
            return log;
        }
    }
}
