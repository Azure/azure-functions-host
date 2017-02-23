// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using Microsoft.Azure.WebJobs.Logging;
using Microsoft.Azure.WebJobs.Protocols;
using Newtonsoft.Json;

namespace Dashboard.Data
{
    public class FunctionInstanceSnapshot
    {
        private string _displayTitle;

        public Guid Id { get; set; }

        public Guid HostInstanceId { get; set; }

        public string InstanceQueueName { get; set; }

        public HeartbeatDescriptor Heartbeat { get; set; }

        public string FunctionId { get; set; }

        public string FunctionFullName { get; set; }

        public string FunctionShortName { get; set; }

        [SuppressMessage("Microsoft.Usage", "CA2227:CollectionPropertiesShouldBeReadOnly")]
        public IDictionary<string, FunctionInstanceArgument> Arguments { get; set; }

        [SuppressMessage("Microsoft.Usage", "CA2227:CollectionPropertiesShouldBeReadOnly")]
        public IDictionary<string, ParameterLog> ParameterLogs { get; set; }

        public Guid? ParentId { get; set; }

        public string Reason { get; set; }

        public DateTimeOffset QueueTime { get; set; }

        public DateTimeOffset? StartTime { get; set; }

        public DateTimeOffset? EndTime { get; set; }

        // For fast-style logging. 
        // This heartbeat is for the function-instance (not for the host).  
        public DateTime? FunctionInstanceHeartbeatExpiry { get; set; }

        // If specified, text output is provided in the string. Ignore the OutputBlob. 
        public string InlineOutputText { get; set; }

        public LocalBlobDescriptor OutputBlob { get; set; }

        public LocalBlobDescriptor ParameterLogBlob { get; set; }

        [SuppressMessage("Microsoft.Naming", "CA1702:CompoundWordsShouldBeCasedCorrectly", MessageId = "WebSite")]
        public string WebSiteName { get; set; }

        public string WebJobType { get; set; }

        public string WebJobName { get; set; }

        public string WebJobRunId { get; set; }

        public bool? Succeeded { get; set; }

        public string ExceptionType { get; set; }

        public string ExceptionMessage { get; set; }

        [JsonIgnore]
        public string DisplayTitle
        {
            get
            {
                if (_displayTitle == null)
                {
                    _displayTitle = BuildFunctionDisplayTitle();
                }
                return _displayTitle;
            }
            set
            {
                _displayTitle = value;
            }
        }

        private string BuildFunctionDisplayTitle()
        {
            IEnumerable<string> argumentValues = Arguments.Values.Select(v => v.Value);
            return FunctionInstanceLogItem.BuildFunctionDisplayTitle(this.FunctionShortName, argumentValues);            
        }
    }
}
