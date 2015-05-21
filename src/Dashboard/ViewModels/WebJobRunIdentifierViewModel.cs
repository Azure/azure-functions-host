// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

namespace Dashboard.ViewModels
{
    public class WebJobRunIdentifierViewModel
    {
        internal WebJobRunIdentifierViewModel(WebJobType jobType, string jobName, string jobRunId)
        {
            JobType = jobType;
            JobName = jobName;
            RunId = jobRunId;
        }

        public WebJobType JobType { get; set; }

        public string JobName { get; set; }

        public string RunId { get; set; }
    }
}
