using System;
using System.Diagnostics;
using System.Globalization;
using Microsoft.WindowsAzure.Jobs.Host;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace Microsoft.WindowsAzure.Jobs
{
    [DebuggerDisplay("{GetKey(),nq}")]
    internal class WebJobRunIdentifier
    {
        static WebJobRunIdentifier()
        {
            var webSiteName = Environment.GetEnvironmentVariable(WebSitesKnownKeyNames.WebSiteNameKey);
            var jobName = Environment.GetEnvironmentVariable(WebSitesKnownKeyNames.JobNameKey);
            var jobTypeName = Environment.GetEnvironmentVariable(WebSitesKnownKeyNames.JobTypeKey);
            var jobRunId = Environment.GetEnvironmentVariable(WebSitesKnownKeyNames.JobRunIdKey);

            WebJobTypes webJobType;
            var isValidWebJobType = Enum.TryParse(jobTypeName, true, out webJobType);

            if (webSiteName == null || !isValidWebJobType || jobName == null)
            {
                _current = null;
            }
            else
            {
                _current = new WebJobRunIdentifier(webSiteName, webJobType, jobName, jobRunId);
            }
        }

        // don't use it - this is only for json.net
        public WebJobRunIdentifier()
        {
            
        }

        public WebJobRunIdentifier(WebJobTypes jobType, string jobName, string runId)
            : this(Environment.GetEnvironmentVariable(WebSitesKnownKeyNames.WebSiteNameKey), jobType, jobName, runId)
        {
        }

        public WebJobRunIdentifier(string websiteName, WebJobTypes jobType, string jobName, string runId)
        {
            if (jobType == WebJobTypes.Triggered && runId == null)
            {
                throw new ArgumentNullException("runId", "runId is required for triggered jobs.");
            }

            WebSiteName = websiteName;
            JobType = jobType;
            JobName = jobName;
            RunId = runId;
        }

        public string WebSiteName { get; set; }
        [JsonConverter(typeof(StringEnumConverter))]
        public WebJobTypes JobType { get; set; }
        public string JobName { get; set; }
        public string RunId { get; set; }

        private static readonly WebJobRunIdentifier _current;

        public static WebJobRunIdentifier Current
        {
            get { return _current; }
        }

        public string GetKey()
        {
            return String.Format(CultureInfo.CurrentCulture, "{0}${1}${2}${3}",
                WebSiteName, JobType, JobName, RunId).ToLowerInvariant();
        }
    }
}