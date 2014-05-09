using System;
using System.Collections.Generic;
using System.Text;
using Microsoft.Azure.Jobs;
using Microsoft.Azure.Jobs.Host;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace Dashboard.ViewModels
{
    public class DashboardIndexViewModel
    {
        public IEnumerable<InvocationLogViewModel> InvocationLogViewModels { get; set; }
        public IEnumerable<FunctionStatisticsViewModel> FunctionStatisticsViewModels { get; set; }
        public string StorageAccountName { get; set; }
    }

    public class WebJobRunIdentifierViewModel
    {
        internal WebJobRunIdentifierViewModel(WebJobRunIdentifier id)
        {
            JobType = (WebJobTypes)id.JobType;
            JobName = id.JobName;
            RunId = id.RunId;
        }

        [JsonConverter(typeof(StringEnumConverter))]
        public WebJobTypes JobType { get; set; }

        public string JobName { get; set; }

        public string RunId { get; set; }
    }

    public class InvocationLogViewModel
    {
        internal InvocationLogViewModel(ExecutionInstanceLogEntity log, DateTimeOffset? heartbeat)
        {
            Id = log.FunctionInstance.Id;
            FunctionName = log.FunctionInstance.Location.GetShortName();
            FunctionFullName = log.FunctionInstance.Location.ToString();
            FunctionDisplayTitle = BuildFunctionDisplayTitle(log.FunctionInstance);
            HostInstanceId = log.HostInstanceId;
            if (log.ExecutingJobRunId != null &&
                log.ExecutingJobRunId.WebSiteName == Environment.GetEnvironmentVariable(WebSitesKnownKeyNames.WebSiteNameKey))
            {
                ExecutingJobRunId = new WebJobRunIdentifierViewModel(log.ExecutingJobRunId);
            }
            DateTime? heartbeatExpires;
            if (heartbeat.HasValue)
            {
                heartbeatExpires = heartbeat.Value.UtcDateTime.Add(RunningHost.HeartbeatPollInterval);
                Status = (FunctionInstanceStatus)log.GetStatusWithHeartbeat(heartbeatExpires);
            }
            else
            {
                heartbeatExpires = null;
                Status = (FunctionInstanceStatus)log.GetStatusWithoutHeartbeat();
            }
            switch (Status)
            {
                case FunctionInstanceStatus.Running:
                    WhenUtc = log.StartTime;
                    Duration = DateTime.UtcNow - log.StartTime;
                    break;
                case FunctionInstanceStatus.CompletedSuccess:
                    WhenUtc = log.EndTime;
                    Duration = log.GetFinalDuration();
                    break;
                case FunctionInstanceStatus.CompletedFailed:
                    WhenUtc = log.EndTime;
                    Duration = log.GetFinalDuration();
                    ExceptionType = log.ExceptionType;
                    ExceptionMessage = log.ExceptionMessage;
                    break;
                case FunctionInstanceStatus.NeverFinished:
                    WhenUtc = log.StartTime;
                    Duration = heartbeatExpires - log.StartTime;
                    break;
            }
        }

        public WebJobRunIdentifierViewModel ExecutingJobRunId { get; set; }

        private string BuildFunctionDisplayTitle(FunctionInvokeRequest functionInstance)
        {
            var name = new StringBuilder(functionInstance.Location.GetShortName());
            if (functionInstance.ParametersDisplayText != null)
            {
                name.Append(" (");
                if (functionInstance.ParametersDisplayText.Length > 20)
                {
                    name.Append(functionInstance.ParametersDisplayText.Substring(0, 18))
                        .Append(" ...");
                }
                else
                {
                    name.Append(functionInstance.ParametersDisplayText);
                }
                name.Append(")");
            }
            return name.ToString();
        }

        public Guid Id { get; set; }
        public string FunctionName { get; set; }
        public string FunctionFullName { get; set; }
        public string FunctionDisplayTitle { get; set; }
        [JsonConverter(typeof(StringEnumConverter))]
        public FunctionInstanceStatus Status { get; set; }
        public DateTime? WhenUtc { get; set; }
        [JsonConverter(typeof(DurationAsMillisecondsJsonConverter))]
        public TimeSpan? Duration { get; set; }
        public string ExceptionMessage { get; set; }
        public string ExceptionType { get; set; }
        public Guid HostInstanceId { get; set; }
        public bool IsFinal()
        {
            return Status == FunctionInstanceStatus.CompletedFailed || Status == FunctionInstanceStatus.CompletedSuccess;
        }
    }

    public class FunctionStatisticsViewModel
    {
        public string FunctionFullName { get; set; }
        public string FunctionName { get; set; }
        public int SuccessCount { get; set; }
        public int FailedCount { get; set; }
        public bool IsRunning { get; set; }
        public bool IsOldHost { get; set; }
        public DateTime? LastStartTime { get; set; }
    }

    public class DurationAsMillisecondsJsonConverter : JsonConverter
    {
        public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
        {
            var timespan = (TimeSpan)value;
            writer.WriteValue(timespan.TotalMilliseconds);
        }

        public override object ReadJson(JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer)
        {
            throw new NotImplementedException();
        }

        public override bool CanConvert(Type objectType)
        {
            return objectType == typeof(TimeSpan);
        }
    }
}
