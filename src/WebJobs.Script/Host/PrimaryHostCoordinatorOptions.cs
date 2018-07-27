// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;

namespace Microsoft.Azure.WebJobs.Script
{
    public class PrimaryHostCoordinatorOptions
    {
        private TimeSpan _leaseTimeout = TimeSpan.FromSeconds(15);

        public TimeSpan LeaseTimeout
        {
            get
            {
                return _leaseTimeout;
            }

            set
            {
                if (value < TimeSpan.FromSeconds(15) || value > TimeSpan.FromSeconds(60))
                {
                    throw new ArgumentOutOfRangeException(nameof(LeaseTimeout), $"The {nameof(LeaseTimeout)} should be between 15 and 60 seconds but was '{value}'");
                }

                _leaseTimeout = value;
            }
        }

        public TimeSpan? RenewalInterval { get; set; } = null;
    }
}
