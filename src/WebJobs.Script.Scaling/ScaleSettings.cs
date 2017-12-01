// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Diagnostics.CodeAnalysis;

namespace Microsoft.Azure.WebJobs.Script.Scaling
{
    [SuppressMessage("Microsoft.Naming", "CA1704:IdentifiersShouldBeSpelledCorrectly", Justification = "By design")]
    public class ScaleSettings
    {
        public const int DefaultMaxWorkers = 100;
        public const int DefaultStableWorkerLoadFactor = 50;
        public const int DefaultBusyWorkerLoadFactor = 80;
        public const double DefaultMaxBusyWorkerRatio = 0.8;
        public const int DefaultFreeWorkerLoadFactor = 20;
        public const double DefaultMaxFreeWorkerRatio = 0.3;
        public static readonly TimeSpan DefaultWorkerUpdateInterval = TimeSpan.FromSeconds(10);
        public static readonly TimeSpan DefaultWorkerPingInterval = TimeSpan.FromSeconds(300);
        public static readonly TimeSpan DefaultScaleCheckInterval = TimeSpan.FromSeconds(10);
        public static readonly TimeSpan DefaultManagerCheckInterval = TimeSpan.FromSeconds(60);
        public static readonly TimeSpan DefaultStaleWorkerCheckInterval = TimeSpan.FromSeconds(120);

        [SuppressMessage("Microsoft.Security", "CA2104:DoNotDeclareReadOnlyMutableReferenceTypes", Justification = "By design")]
        public static readonly ScaleSettings Instance = new ScaleSettings();

        public ScaleSettings()
        {
            MaxWorkers = DefaultMaxWorkers;
            BusyWorkerLoadFactor = DefaultBusyWorkerLoadFactor;
            MaxBusyWorkerRatio = DefaultMaxBusyWorkerRatio;
            FreeWorkerLoadFactor = DefaultFreeWorkerLoadFactor;
            MaxFreeWorkerRatio = DefaultMaxFreeWorkerRatio;
            WorkerUpdateInterval = DefaultWorkerUpdateInterval;
            WorkerPingInterval = DefaultWorkerPingInterval;
            ScaleCheckInterval = DefaultScaleCheckInterval;
            ManagerCheckInterval = DefaultManagerCheckInterval;
            StaleWorkerCheckInterval = DefaultStaleWorkerCheckInterval;
        }

        /// <summary>
        /// Gets or sets maximum number of workers
        /// </summary>
        public int MaxWorkers
        {
            get;
            set;
        }

        /// <summary>
        /// Gets or sets busy worker loadfactor
        /// </summary>
        public double BusyWorkerLoadFactor
        {
            get;
            set;
        }

        /// <summary>
        /// Gets or sets busy worker ratio before adding worker
        /// </summary>
        public double MaxBusyWorkerRatio
        {
            get;
            set;
        }

        /// <summary>
        /// Gets or sets free worker loadfactor
        /// </summary>
        public double FreeWorkerLoadFactor
        {
            get;
            set;
        }

        /// <summary>
        /// Gets or sets free worker ratio before removing worker
        /// </summary>
        public double MaxFreeWorkerRatio
        {
            get;
            set;
        }

        /// <summary>
        /// Gets or sets update worker interval
        /// </summary>
        public TimeSpan WorkerUpdateInterval
        {
            get;
            set;
        }

        /// <summary>
        /// Gets or sets ping worker interval
        /// </summary>
        public TimeSpan WorkerPingInterval
        {
            get;
            set;
        }

        /// <summary>
        /// Gets or sets how often to ensure manager selection
        /// </summary>
        public TimeSpan ManagerCheckInterval
        {
            get;
            set;
        }

        /// <summary>
        /// Gets or sets how often to perform stale worker cleanup
        /// </summary>
        public TimeSpan StaleWorkerCheckInterval
        {
            get;
            set;
        }

        /// <summary>
        /// Gets or sets how often to perform stale worker cleanup
        /// </summary>
        public TimeSpan ScaleCheckInterval
        {
            get;
            set;
        }
    }
}
