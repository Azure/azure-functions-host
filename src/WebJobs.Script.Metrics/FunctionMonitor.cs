using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.Serialization;
using System.Threading;

namespace Microsoft.Azure.WebJobs.Script.Metrics
{
    public class FunctionMonitor : IFunctionMonitor, IFunctionExecutionMonitor
    {
        private const string MetricNamespace = "Microsoft/Web/WebApps";

        private readonly IAnalyticsPublisher _analyticsPublisher;
        private readonly IMetricsPublisher _metricsPublisher;
        private Process _process;
        private FunctionContainerActivity _currentFunctionContainerActivity;
        private FunctionContainerActivity _lastFunctionContainerActivity;
        private const int FunctionContainerMemoryLimitInMb = 128;
        private Timer _functionActivityTimer;
        private Timer _processMonitorTimer;
        private Timer _functionMetricsTimer;
        private object _lock = new object();

        public FunctionMonitor(IMetricsPublisher metricsPublisher, IAnalyticsPublisher analyticsPublisher)
        {
            _metricsPublisher = metricsPublisher;
            _analyticsPublisher = analyticsPublisher;
        }

        public void Start()
        {
            _process = Process.GetCurrentProcess();
            _currentFunctionContainerActivity = CreateNewActivity();
            _lastFunctionContainerActivity = new FunctionContainerActivity();
            _currentFunctionContainerActivity.CopyTo(_lastFunctionContainerActivity);

            _functionActivityTimer = new Timer(OnFunctionActivityTimer, null, TimeSpan.Zero, TimeSpan.FromMilliseconds(100));
            _processMonitorTimer = new Timer(OnProcessMonitorTimer, null, TimeSpan.Zero, TimeSpan.FromMilliseconds(1000));
            _functionMetricsTimer = new Timer(OnMetricsTimer, null, TimeSpan.Zero, TimeSpan.FromSeconds(10));
        }

        public void Stop()
        {
            _functionActivityTimer.Change(Timeout.Infinite, Timeout.Infinite);
            _processMonitorTimer.Change(Timeout.Infinite, Timeout.Infinite);
            _processMonitorTimer.Change(Timeout.Infinite, Timeout.Infinite);
        }

        private void OnMetricsTimer(object state)
        {
            FunctionContainerActivity activitySnapshot = null;
            lock (_lock)
            {
                if (!_currentFunctionContainerActivity.Equals(_lastFunctionContainerActivity))
                {
                    activitySnapshot = _currentFunctionContainerActivity.TakeSnapshot(_lastFunctionContainerActivity, _analyticsPublisher);
                }
            }

            if (activitySnapshot != null)
            {
                // TODO: any other events to publish?

                // publish the data
                _metricsPublisher.Publish(activitySnapshot.TimestampUtc, MetricNamespace, "FunctionExecutionTime", activitySnapshot.FunctionExecutionTimeInMs);
                _metricsPublisher.Publish(activitySnapshot.TimestampUtc, MetricNamespace, "FunctionExecutionUnits", activitySnapshot.FunctionExecutionUnits);
                _metricsPublisher.Publish(activitySnapshot.TimestampUtc, MetricNamespace, "FunctionExecutionCount", activitySnapshot.FunctionExecutionCount);
                //_metricsPublisher.Publish(activitySnapshot.TimestampUtc, MetricNamespace, "FunctionContainerSize", activitySnapshot.FunctionContainerSizeInMb);
            }
        }

        private void OnProcessMonitorTimer(object state)
        {
            var commitSizeBytes = _process.PrivateMemorySize64;
            if (commitSizeBytes != 0)
            {
                lock (_lock)
                {
                    _currentFunctionContainerActivity.UpdateMemorySnapshotList(commitSizeBytes, DateTime.UtcNow, _analyticsPublisher);
                }
            }
        }

        /// <summary>
        /// When the timer fires, we perform function execution unit calculations and emit
        /// metrics.
        /// </summary>
        /// <param name="state"></param>
        private void OnFunctionActivityTimer(object state)
        {
            if (_currentFunctionContainerActivity != null &&
                _currentFunctionContainerActivity.FunctionContainerSizeInMb != 0 &&
                _currentFunctionContainerActivity.MemorySnapshots != null)
            {
                lock (_lock)
                {
                    _currentFunctionContainerActivity.CalculateNextFunctionExecutionUnits(_analyticsPublisher);
                }
            }
        }

        /// <summary>
        /// Called whenever status changes for an active function execution.
        /// </summary>
        /// <param name="status">Teh current status of the execution.</param>
        public void FunctionExecution(FunctionExecutionStatus status)
        {
            var activity = new FunctionActivity
            {
                ProcessId = status.ProcessId,
                ExecutionId = status.ExecutionId,
                FunctionName = status.FunctionName,
                InvocationId = status.InvocationId,
                CurrentExecutionStage = status.CurrentExecutionStage,
                ExecutionTimeSpanInMs = status.ExecutionTimeSpanInMs,
                Concurrency = status.Concurrency,
                IsSucceeded = status.IsSucceeded,
                StartTime = Utility.TrimSubMilliseconds(DateTime.UtcNow.AddMilliseconds(-1 * ((long)status.ExecutionTimeSpanInMs)))
            };

            lock (_lock)
            {
                _currentFunctionContainerActivity.UpdateFunctionActivity(activity, _analyticsPublisher);
            }
        }

        private FunctionContainerActivity CreateNewActivity()
        {
            // TODO: set site info, etc.
            var activity = new FunctionContainerActivity()
            {
                FunctionContainerSizeInMb = FunctionContainerMemoryLimitInMb
            };
            return activity;
        }

        [DataContract]
        [KnownType(typeof(FunctionContainerActivity))]
        [KnownType(typeof(FunctionActivity))]
        internal class Metric
        {
            public static Type[] KnownTypes = new[] { typeof(FunctionContainerActivity), typeof(FunctionActivity) };

            [DataMember]
            public string ServerName { get; set; }

            [DataMember(EmitDefaultValue = false)]
            public DateTime TimestampUtc { get; set; }

            public Metric()
            {
                this.ServerName = Environment.MachineName;
                this.TimestampUtc = DateTime.UtcNow;
            }

            public override string ToString()
            {
                return ServerName;
            }
        }

        [DebuggerDisplay("SiteName = {SiteName}, HasExit = {HasExit}")]
        [DataContract]
        internal sealed class FunctionContainerActivity : Metric, IEquatable<FunctionContainerActivity>, IFunctionContainerActivity, ICloneable
        {
            [DataMember]
            public string SiteName { get; set; }
            [DataMember]
            public int[] ProcessIds { get; set; }
            [DataMember]
            public bool HasExit { get; set; }
            [DataMember]
            public TimeSpan UserTime { get; set; }
            [DataMember]
            public TimeSpan KernelTime { get; set; }
            [DataMember]
            public long MemoryPeakWorkingSet { get; set; }
            [DataMember]
            public long MemoryWorkingSet { get; set; }
            [DataMember]
            public long NetworkBytesSent { get; set; }
            [DataMember]
            public long NetworkBytesReceived { get; set; }
            [DataMember]
            public long LocalBytesRead { get; set; }
            [DataMember]
            public long LocalBytesWritten { get; set; }
            [DataMember]
            public long NetworkBytesRead { get; set; }
            [DataMember]
            public long NetworkBytesWritten { get; set; }
            [DataMember]
            public int StopRequests { get; set; }
            [DataMember]
            public int FunctionContainerSizeInMb { get; set; }
            [DataMember]
            public long FunctionExecutionTimeInMs { get; set; }
            [DataMember]
            public long FunctionExecutionCount { get; set; }
            [DataMember]
            public IDictionary<string, IFunctionActivity> ActiveFunctionActivities { get; set; }
            [DataMember]
            public DateTime LastFunctionUpdateTimeStamp { get; set; }
            [DataMember]
            public long FunctionExecutionUnits { get; set; }
            [DataMember]
            public DateTime LastFunctionExecutionCalcuationMemorySnapshotTime { get; set; }
            [DataMember]
            public int LastMemoryBucketSize { get; set; }
            [DataMember]
            public SortedList<DateTime, double> MemorySnapshots { get; set; }

            [DataMember]
            public string DefaultHostName { get; set; }

            [DataMember]
            public FunctionExecutionStage? PreviousPublishedStatus { get; set; }

            [IgnoreDataMember]
            public string PlaceholderSiteName { get; set; }

            private readonly TimeSpan FunctionStatusForcePublishInterval = TimeSpan.FromHours(1);

            public FunctionContainerActivity()
            {
                this.LastFunctionUpdateTimeStamp = DateTime.UtcNow;
                this.LastFunctionExecutionCalcuationMemorySnapshotTime = DateTime.MinValue;
                this.LastMemoryBucketSize = 0;
            }

            public FunctionContainerActivity TakeSnapshot(FunctionContainerActivity lastActivity, IAnalyticsPublisher analyticsPublisher)
            {
                var activitySnapshot = new FunctionContainerActivity
                {
                    SiteName = SiteName,
                    TimestampUtc = TimestampUtc,
                    UserTime = TimeSpan.FromTicks(UserTime.Ticks - lastActivity.UserTime.Ticks),
                    KernelTime = TimeSpan.FromTicks(KernelTime.Ticks - lastActivity.KernelTime.Ticks),
                    MemoryWorkingSet = MemoryWorkingSet,
                    MemoryPeakWorkingSet = MemoryPeakWorkingSet,
                    NetworkBytesSent = NetworkBytesSent - lastActivity.NetworkBytesSent,
                    NetworkBytesReceived = NetworkBytesReceived - lastActivity.NetworkBytesReceived,
                    LocalBytesRead = LocalBytesRead - lastActivity.LocalBytesRead,
                    LocalBytesWritten = LocalBytesWritten - lastActivity.LocalBytesWritten,
                    NetworkBytesRead = NetworkBytesRead - lastActivity.NetworkBytesRead,
                    NetworkBytesWritten = NetworkBytesWritten - lastActivity.NetworkBytesWritten,
                    StopRequests = StopRequests - lastActivity.StopRequests,
                    FunctionContainerSizeInMb = FunctionContainerSizeInMb,
                    FunctionExecutionTimeInMs = FunctionExecutionTimeInMs - lastActivity.FunctionExecutionTimeInMs,
                    FunctionExecutionCount = FunctionExecutionCount - lastActivity.FunctionExecutionCount,
                    FunctionExecutionUnits = FunctionExecutionUnits - lastActivity.FunctionExecutionUnits,
                    LastFunctionUpdateTimeStamp = LastFunctionUpdateTimeStamp,
                    DefaultHostName = DefaultHostName,
                    PreviousPublishedStatus = PreviousPublishedStatus
                };

                CopyTo(lastActivity);

                this.RemoveStaleFunctionExecutions(analyticsPublisher);

                return activitySnapshot;
            }

            public void CopyTo(FunctionContainerActivity other)
            {
                if (other == null)
                {
                    throw new ArgumentNullException("other");
                }

                // SiteName should not be copied.

                other.TimestampUtc = TimestampUtc;
                //other.HasExit = HasExit;
                other.UserTime = UserTime;
                other.KernelTime = KernelTime;
                other.MemoryPeakWorkingSet = MemoryPeakWorkingSet;
                other.MemoryWorkingSet = MemoryWorkingSet;
                other.NetworkBytesSent = NetworkBytesSent;
                other.NetworkBytesReceived = NetworkBytesReceived;
                other.LocalBytesRead = LocalBytesRead;
                other.LocalBytesWritten = LocalBytesWritten;
                other.NetworkBytesRead = NetworkBytesRead;
                other.NetworkBytesWritten = NetworkBytesWritten;
                other.StopRequests = StopRequests;
                other.FunctionContainerSizeInMb = FunctionContainerSizeInMb;
                other.FunctionExecutionTimeInMs = FunctionExecutionTimeInMs;
                other.FunctionExecutionCount = FunctionExecutionCount;
                other.LastFunctionUpdateTimeStamp = LastFunctionUpdateTimeStamp;
                other.ActiveFunctionActivities = ActiveFunctionActivities;
                other.FunctionExecutionUnits = FunctionExecutionUnits;
                other.MemorySnapshots = MemorySnapshots;
                other.LastFunctionExecutionCalcuationMemorySnapshotTime = LastFunctionExecutionCalcuationMemorySnapshotTime;
                other.LastMemoryBucketSize = LastMemoryBucketSize;
                other.DefaultHostName = DefaultHostName;
                other.PreviousPublishedStatus = PreviousPublishedStatus;
            }

            public override string ToString()
            {
                return
                    "[SiteName = " + SiteName +
                    " HasExit = " + HasExit +
                    " UserTime = " + UserTime +
                    " KernelTime = " + KernelTime +
                    " MemoryPeakWorkingSet = " + MemoryPeakWorkingSet +
                    " MemoryWorkingSet = " + MemoryWorkingSet +
                    " NetworkBytesSent = " + NetworkBytesSent +
                    " NetworkBytesReceived = " + NetworkBytesReceived +
                    " LocalBytesRead = " + LocalBytesRead +
                    " LocalBytesWritten = " + LocalBytesWritten +
                    " NetworkBytesRead = " + NetworkBytesRead +
                    " NetworkBytesWritten = " + NetworkBytesWritten +
                    " StopRequests = " + StopRequests +
                    " FunctionContainerSizeInMb = " + FunctionContainerSizeInMb +
                    " FunctionExecutionTimeInMs = " + FunctionExecutionTimeInMs +
                    " FunctionExecutionCount = " + FunctionExecutionCount +
                    " LastFunctionUpdateTimeStamp = " + LastFunctionUpdateTimeStamp +
                    " FunctionExecutionUnits = " + FunctionExecutionUnits +
                    " LastFunctionExecutionCalcuationMemorySnapshotTime = " + LastFunctionExecutionCalcuationMemorySnapshotTime +
                    " LastMemoryBucketSize = " + LastMemoryBucketSize +
                    " DefaultHostName = " + DefaultHostName +
                    " PreviousPublishedStatus = " + PreviousPublishedStatus.GetValueOrDefault(FunctionExecutionStage.Finished) +
                    "]";
            }

            public bool Equals(FunctionContainerActivity other)
            {
                if (other == null)
                {
                    return false;
                }

                // Don't take the last update time in consideration.
                bool value = UserTime == other.UserTime &&
                        KernelTime == other.KernelTime &&
                        MemoryPeakWorkingSet == other.MemoryPeakWorkingSet &&
                        MemoryWorkingSet == other.MemoryWorkingSet &&
                        NetworkBytesSent == other.NetworkBytesSent &&
                        NetworkBytesReceived == other.NetworkBytesReceived &&
                        LocalBytesRead == other.LocalBytesRead &&
                        LocalBytesWritten == other.LocalBytesWritten &&
                        NetworkBytesRead == other.NetworkBytesRead &&
                        NetworkBytesWritten == other.NetworkBytesWritten &&
                        StopRequests == other.StopRequests &&
                        FunctionContainerSizeInMb == other.FunctionContainerSizeInMb &&
                        FunctionExecutionTimeInMs == other.FunctionExecutionTimeInMs &&
                        FunctionExecutionCount == other.FunctionExecutionCount &&
                        FunctionExecutionUnits == other.FunctionExecutionUnits &&
                        string.Equals(DefaultHostName, other.DefaultHostName, StringComparison.OrdinalIgnoreCase) &&
                        PreviousPublishedStatus == other.PreviousPublishedStatus;

                return value;
            }

            public object Clone()
            {
                FunctionContainerActivity clone = new FunctionContainerActivity
                {
                    SiteName = this.SiteName,
                    // By reference.
                    ProcessIds = this.ProcessIds,
                    HasExit = this.HasExit,
                    TimestampUtc = this.TimestampUtc,
                    UserTime = this.UserTime,
                    KernelTime = this.KernelTime,
                    MemoryPeakWorkingSet = this.MemoryPeakWorkingSet,
                    MemoryWorkingSet = this.MemoryWorkingSet,
                    NetworkBytesSent = this.NetworkBytesSent,
                    NetworkBytesReceived = this.NetworkBytesReceived,
                    LocalBytesRead = this.LocalBytesRead,
                    LocalBytesWritten = this.LocalBytesWritten,
                    NetworkBytesRead = this.NetworkBytesRead,
                    NetworkBytesWritten = this.NetworkBytesWritten,
                    StopRequests = this.StopRequests,
                    FunctionContainerSizeInMb = this.FunctionContainerSizeInMb,
                    FunctionExecutionTimeInMs = this.FunctionExecutionTimeInMs,
                    FunctionExecutionCount = this.FunctionExecutionCount,
                    LastFunctionUpdateTimeStamp = this.LastFunctionUpdateTimeStamp,
                    ActiveFunctionActivities = this.ActiveFunctionActivities,
                    FunctionExecutionUnits = this.FunctionExecutionUnits,
                    LastFunctionExecutionCalcuationMemorySnapshotTime = this.LastFunctionExecutionCalcuationMemorySnapshotTime,
                    MemorySnapshots = this.MemorySnapshots,
                    LastMemoryBucketSize = this.LastMemoryBucketSize,
                    DefaultHostName = this.DefaultHostName,
                    PreviousPublishedStatus = this.PreviousPublishedStatus
                };

                return clone;
            }
        }

        // represents the status of a single function invocation
        [DataContract]
        internal sealed class FunctionActivity : ICloneable, IFunctionActivity
        {
            [DataMember]
            public int ProcessId { get; set; }

            [DataMember]
            public string ExecutionId { get; set; }

            [DataMember]
            public string FunctionName { get; set; }

            [DataMember]
            public string InvocationId { get; set; }

            [DataMember]
            public FunctionExecutionStage CurrentExecutionStage { get; set; }

            [DataMember]
            public long ExecutionTimeSpanInMs { get; set; }

            [DataMember]
            public long ActualExecutionTimeSpanInMs { get; set; }

            [DataMember]
            public int Concurrency { get; set; }

            [DataMember]
            public bool IsSucceeded { get; set; }

            [DataMember]
            public DateTime LastUpdatedTimeUtc { get; set; }

            [DataMember]
            public DateTime StartTime { get; set; }

            [DataMember]
            public List<DynamicMemoryBucketCalculation> DynamicMemoryBucketCalculations { get; set; }

            public FunctionActivity()
            {
                DynamicMemoryBucketCalculations = new List<DynamicMemoryBucketCalculation>();
            }

            public DateTime TrimmedStartTime
            {
                get
                {
                    return Utility.TrimSubMilliseconds(StartTime);
                }
            }

            public DateTime EndTime
            {
                get
                {
                    return Utility.TrimSubMilliseconds(StartTime.AddMilliseconds(ExecutionTimeSpanInMs));
                }
            }

            public object Clone()
            {
                FunctionActivity clone = new FunctionActivity
                {
                    ProcessId = this.ProcessId,
                    ExecutionId = this.ExecutionId,
                    FunctionName = this.FunctionName,
                    InvocationId = this.InvocationId,
                    CurrentExecutionStage = this.CurrentExecutionStage,
                    ExecutionTimeSpanInMs = this.ExecutionTimeSpanInMs,
                    ActualExecutionTimeSpanInMs = this.ActualExecutionTimeSpanInMs,
                    Concurrency = this.Concurrency,
                    IsSucceeded = this.IsSucceeded,
                    LastUpdatedTimeUtc = this.LastUpdatedTimeUtc,
                    StartTime = this.StartTime
                };

                return clone;
            }

            public void CopyFrom(IFunctionActivity other)
            {
                this.ProcessId = other.ProcessId;
                this.ExecutionId = other.ExecutionId;
                this.FunctionName = other.FunctionName;
                this.InvocationId = other.InvocationId;
                this.CurrentExecutionStage = other.CurrentExecutionStage;
                this.ExecutionTimeSpanInMs = other.ExecutionTimeSpanInMs;
                this.ActualExecutionTimeSpanInMs = other.ActualExecutionTimeSpanInMs;
                this.Concurrency = other.Concurrency;
                this.IsSucceeded = other.IsSucceeded;
                this.LastUpdatedTimeUtc = DateTime.UtcNow;
            }
        }
    }
}
