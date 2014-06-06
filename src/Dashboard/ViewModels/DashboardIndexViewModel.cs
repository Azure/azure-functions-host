using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Dashboard.Data;
using Microsoft.Azure.Jobs;
using Microsoft.Azure.Jobs.Protocols;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

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

        [JsonConverter(typeof(StringEnumConverter))]
        public WebJobTypes JobType { get; set; }

        public string JobName { get; set; }

        public string RunId { get; set; }
    }

    public class InvocationLogViewModel
    {
        internal InvocationLogViewModel(FunctionInstanceSnapshot snapshot, bool? heartbeatIsValid)
        {
            Id = snapshot.Id;
            FunctionName = snapshot.FunctionShortName;
            FunctionId = snapshot.FunctionId;
            FunctionFullName = snapshot.FunctionFullName;
            FunctionDisplayTitle = BuildFunctionDisplayTitle(snapshot);
            HostInstanceId = snapshot.HostInstanceId;
            if (snapshot.WebSiteName != null
                && snapshot.WebSiteName == Environment.GetEnvironmentVariable(WebSitesKnownKeyNames.WebSiteNameKey))
            {
                ExecutingJobRunId = new WebJobRunIdentifierViewModel((WebJobTypes)Enum.Parse(typeof(WebJobTypes),
                    snapshot.WebJobType), snapshot.WebJobName, snapshot.WebJobRunId);
            }
            if (heartbeatIsValid.HasValue)
            {
                Status = snapshot.GetStatusWithHeartbeat(heartbeatIsValid.Value);
            }
            else
            {
                Status = snapshot.GetStatusWithoutHeartbeat();
            }
            switch (Status)
            {
                case FunctionInstanceStatus.Running:
                    WhenUtc = snapshot.StartTime.Value.UtcDateTime;
                    Duration = DateTimeOffset.UtcNow - snapshot.StartTime;
                    break;
                case FunctionInstanceStatus.CompletedSuccess:
                    WhenUtc = snapshot.EndTime.Value.UtcDateTime;
                    Duration = snapshot.GetFinalDuration();
                    break;
                case FunctionInstanceStatus.CompletedFailed:
                    WhenUtc = snapshot.EndTime.Value.UtcDateTime;
                    Duration = snapshot.GetFinalDuration();
                    ExceptionType = snapshot.ExceptionType;
                    ExceptionMessage = snapshot.ExceptionMessage;
                    break;
                case FunctionInstanceStatus.NeverFinished:
                    WhenUtc = snapshot.StartTime.Value.UtcDateTime;
                    Duration = null;
                    break;
            }
        }

        public WebJobRunIdentifierViewModel ExecutingJobRunId { get; set; }

        internal static string BuildFunctionDisplayTitle(FunctionInstanceSnapshot snapshot)
        {
            var name = new StringBuilder(snapshot.FunctionShortName);
            IEnumerable<string> argumentValues = snapshot.Arguments.Values.Select(v => v.Value);
            string parametersDisplayText = String.Join(", ", argumentValues);
            if (parametersDisplayText != null)
            {
                name.Append(" (");
                if (parametersDisplayText.Length > 20)
                {
                    name.Append(parametersDisplayText.Substring(0, 18))
                        .Append(" ...");
                }
                else
                {
                    name.Append(parametersDisplayText);
                }
                name.Append(")");
            }
            return name.ToString();
        }

        public Guid Id { get; set; }
        public string FunctionId { get; set; }
        public string FunctionName { get; set; }
        public string FunctionFullName { get; set; }
        public string FunctionDisplayTitle { get; set; }
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
        public string FunctionId { get; set; }
        public string FunctionFullName { get; set; }
        public string FunctionName { get; set; }
        public int SuccessCount { get; set; }
        public int FailedCount { get; set; }
        public bool IsRunning { get; set; }
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
