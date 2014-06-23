using System;
using Microsoft.Azure.Jobs.Protocols;

namespace Dashboard.Data
{
    // Names of directory prefixes used only by the dashboard (not part of the protocol with hosts).
    internal static class DashboardBlobPrefixes
    {
        public static string CreateByFunctionRelativePrefix(string functionId)
        {
            return functionId + "/";
        }

        public static string CreateByJobRunRelativePrefix(WebJobRunIdentifier webJobRunId)
        {
            return webJobRunId.GetKey() + "/";
        }

        public static string CreateByParentRelativePrefix(Guid parentId)
        {
            return parentId.ToString("N") + "/";
        }
    }
}
