// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

namespace Dashboard.ViewModels
{
    public class WebJobRunIdentifierViewModel
    {
        internal WebJobRunIdentifierViewModel(WebJobTypes jobType, string jobName, string jobRunId)
        {
            JobType = jobType;
            JobName = jobName;
            RunId = jobRunId;
        }

        public WebJobTypes JobType { get; set; }

        public string JobName { get; set; }

        public string RunId { get; set; }
    }
}
