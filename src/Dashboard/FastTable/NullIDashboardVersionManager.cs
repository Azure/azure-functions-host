// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;

namespace Dashboard.Data
{
    internal class NullIDashboardVersionManager : IDashboardVersionManager
    {
        public IConcurrentDocument<DashboardVersion> CurrentVersion
        {
            get
            {
                var doc = new DashboardVersion
                {
                    Version = DashboardVersionNumber.Version1,
                    UpgradeState = DashboardUpgradeState.Finished,
                };

                return new SimpleConcurrentDocument<DashboardVersion> { Document = doc };
            }

            set
            {
                throw new NotImplementedException();
            }
        }

        public void FinishUpgrade(string eTag)
        {
            throw new NotImplementedException();
        }

        public IConcurrentDocument<DashboardVersion> Read()
        {
            return this.CurrentVersion;
        }

        public void StartDeletingOldData(string eTag)
        {
            throw new NotImplementedException();
        }

        public void StartRestoringArchive(string eTag)
        {
            throw new NotImplementedException();
        }
    }
}