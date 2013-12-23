using System;
using System.Collections.Generic;
using Microsoft.WindowsAzure.Jobs;

namespace Dashboard.ViewModels
{
    public class DashboardIndexViewModel
    {
        public IEnumerable<InvocationLogViewModel> InvocationLogViewModels { get; set; }
        public IEnumerable<FunctionStatisticsViewModel> FunctionStatisticsViewModels { get; set; }
    }

    public class InvocationLogViewModel
    {
        internal InvocationLogViewModel(ExecutionInstanceLogEntity log)
        {
            Id = log.FunctionInstance.Id;
            FunctionName = log.FunctionInstance.Location.GetShortName();
            FunctionFullName = log.FunctionInstance.Location.ToString();
            Status = (FunctionInstanceStatus)log.GetStatus();
            switch (Status)
            {
                case FunctionInstanceStatus.AwaitingPrereqs:
                    break;
                case FunctionInstanceStatus.Queued:
                    WhenUtc = log.QueueTime;
                    break;
                case FunctionInstanceStatus.Running:
                    Duration = DateTime.UtcNow - log.StartTime.Value;
                    break;
                case FunctionInstanceStatus.CompletedSuccess:
                    WhenUtc = log.EndTime;
                    Duration = log.GetDuration();
                    break;
                case FunctionInstanceStatus.CompletedFailed:
                    Duration = log.GetDuration();
                    break;
                case FunctionInstanceStatus.NeverFinished:
                    Duration = log.GetDuration();
                    break;
            }
        }

        public Guid Id { get; set; }
        public string FunctionName { get; set; }
        public string FunctionFullName { get; set; }
        public FunctionInstanceStatus Status { get; set; }
        public DateTime? WhenUtc { get; set; }
        public TimeSpan? Duration { get; set; }
    }

    public class FunctionStatisticsViewModel
    {
        public string FunctionFullName { get; set; }
        public string FunctionName { get; set; }
        public int SuccessCount { get; set; }
        public int FailedCount { get; set; }
        public bool IsRunning { get; set; }
        public DateTime? LastWriteTime { get; set; }
    }
}