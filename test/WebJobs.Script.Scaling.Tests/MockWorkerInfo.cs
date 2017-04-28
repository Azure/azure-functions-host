// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;

namespace Microsoft.Azure.WebJobs.Script.Scaling.Tests
{
    public class MockWorkerInfo : IWorkerInfo
    {
        public const string TestSiteName = "test-site";
        public const string HomeStampName = "home-stamp";

        public MockWorkerInfo()
        {
            WorkerName = "127.0.0.1";
            StampName = HomeStampName;
            Properties = new Dictionary<string, object>();
        }

        public string SiteName
        {
            get { return TestSiteName; }
        }

        public virtual string WorkerName { get; set; }

        public virtual string StampName { get; set; }

        public virtual int LoadFactor { get; set; }

        public virtual DateTime LastModifiedTimeUtc { get; set; }

        public virtual bool IsStale { get; set; }

        public virtual Dictionary<string, object> Properties { get; set; }

        public bool IsHomeStamp
        {
            get { return StampName == HomeStampName; }
        }
    }
}