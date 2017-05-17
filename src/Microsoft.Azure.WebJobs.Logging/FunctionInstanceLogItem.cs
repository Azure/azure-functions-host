// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Text;

namespace Microsoft.Azure.WebJobs.Logging
{
    /// <summary>
    /// Publically visible. 
    /// Represent a function invocation. 
    /// </summary>
    public class FunctionInstanceLogItem : IFunctionInstanceBaseEntry
    {
        // Max lengths of various fields.
        // Except for output logging, these shouldn't get hit in practice.
        // Truncate if they're exceeded. 
        private const int MaxTriggerReasonLength = 200;
        private const int MaxErrorLength = 500;
        private const int MaxLogOutputLength = 2000 + 3;
        private const int MaxParameterPayloadLength = 1000;
        private const int MaxParameterTotalPayloadLength = 2000;

        /// <summary>Gets or sets the function instance ID.</summary>
        public Guid FunctionInstanceId { get; set; }

        /// <summary>Gets or sets the Function ID of the ancestor function instance.</summary>
        public Guid? ParentId { get; set; }

        /// <summary>Short Name of this function.</summary>
        public string FunctionName { get; set; }

        /// <summary>Globally unique name of this function. This is unique across hosts. 
        /// This can be used in querying other instances of this function. </summary>
        public FunctionId FunctionId { get; set; }

        /// <summary>
        /// An optional hint about why this function was invoked. It may have been triggered, replayed, manually invoked, etc. 
        /// </summary>
        public string TriggerReason { get; set; }

        /// <summary>UTC time that the function started executing.</summary>
        public DateTime StartTime { get; set; }

        /// <summary>If set, the time the function completed (either successfully or failure). </summary>
        public DateTime? EndTime { get; set; }

        /// <summary>A function outputs a heartbeat while it's running. 
        /// If EndTime = null and heartbeat is "old", then the function is likley abandoned (machine killed).
        /// It's always possible a node loses network connectity, and so we appear abandoned, but 
        /// the function successfully completes</summary>
        /// <inheritdoc/>
        public DateTime? FunctionInstanceHeartbeatExpiry { get; set; }

        /// <summary>
        /// Given log item a chance to refresh. 
        /// </summary>
        /// <param name="pollingFrequency">Approximate frequency at which refresh is called. This can be used to determine the heartbeat expiration time. </param>
        public virtual void Refresh(TimeSpan pollingFrequency)
        {
            var gracePeriod = pollingFrequency.TotalMilliseconds * 3;
            this.FunctionInstanceHeartbeatExpiry = DateTime.UtcNow.AddMilliseconds(gracePeriod);
        }

        /// <summary>
        /// Null on success.
        /// Else, set to some string with error details. 
        /// </summary>
        public string ErrorDetails { get; set; }

        /// <summary>
        /// True if we have an error. Implementation of <see cref="IFunctionInstanceBaseEntry"/> 
        /// </summary>
        public bool HasError
        {
            get
            {
                return this.ErrorDetails != null;
            }
        }

        /// <summary>Gets or sets the function's argument values and help strings.</summary>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Usage", "CA2227:CollectionPropertiesShouldBeReadOnly")]
        public IDictionary<string, string> Arguments { get; set; }

        /// <summary>Direct inline capture for small log outputs. For large log outputs, this is faulted over to a blob. </summary>
        public string LogOutput { get; set; }

        /// <summary>
        /// Get a summary for this instance. 
        /// </summary>
        /// <returns></returns>
        public string GetDisplayTitle()
        {
            IEnumerable<string> argumentValues = null;
            if (this.Arguments != null)
            {
                argumentValues = this.Arguments.Values;
            }

            return BuildFunctionDisplayTitle(this.FunctionName, argumentValues);
        }

        /// <summary>
        /// Helper to build an instance summary display name given the arguments. 
        /// </summary>
        /// <param name="functionName">name of the function</param>
        /// <param name="argumentValues">argument values for this instance</param>
        /// <returns></returns>
        public static string BuildFunctionDisplayTitle(string functionName, IEnumerable<string> argumentValues)
        {
            var name = new StringBuilder(functionName);
            if (argumentValues != null)
            {
                string parametersDisplayText = String.Join(", ", argumentValues);
                if (parametersDisplayText != null)
                {
                    // Remove newlines to avoid 403/forbidden storage exceptions when saving display title to blob metadata
                    // for function indexes. Newlines may be present in JSON-formatted arguments.
                    parametersDisplayText = parametersDisplayText.Replace("\r\n", String.Empty);

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
            }

            return name.ToString();
        }

        /// <summary>
        /// Validate the fields are throw if they are inconsistent. 
        /// </summary>
        public void Validate()
        {
            if (this.FunctionInstanceId == Guid.Empty)
            {
                throw new InvalidOperationException("Function Instance Id must be set.");
            }

            if (string.IsNullOrWhiteSpace(this.FunctionName))
            {
                throw new InvalidOperationException("Function Name must be set.");
            }

            if (this.EndTime.HasValue)
            {
                if (this.StartTime > this.EndTime)
                {
                    // This can happen in rare cases if the clock adjusts while the function is running. 
                    this.EndTime = this.StartTime.AddMilliseconds(1);                    
                }
            }
        }
                     
        /// <summary>
        /// Truncate various fields to fit in logging sizes. 
        /// </summary>
        public void Truncate()
        {
            // This is fundamentally driven by performance. We need to fit log entries into table rows.
            // Truncate to ensure that we're under table's maximum request payload size (4mb).
            // None of these limits (except for output log)  should actually get hit in normal scenarios. 
            this.TriggerReason = Truncate(this.TriggerReason, MaxTriggerReasonLength);
            this.ErrorDetails = Truncate(this.ErrorDetails, MaxErrorLength);

            // Logger should already have truncated this, but just in case. 
            this.LogOutput = Truncate(this.LogOutput, MaxLogOutputLength);

            // Arguments may have 1 larger argument for the trigger. 
            // The other arguments should all be small. 
            if (this.Arguments != null)
            {
                bool truncate = false;
                int argSize = 0;
                foreach (var kv in this.Arguments)
                {
                    argSize += kv.Key.Length;
                    if (!string.IsNullOrEmpty(kv.Value))
                    {
                        if (kv.Value.Length > MaxParameterPayloadLength)
                        {
                            truncate = true;
                            argSize += MaxParameterPayloadLength;
                        }
                        else
                        {
                            argSize += kv.Value.Length;
                        }
                    }
                }

                if (argSize > MaxParameterTotalPayloadLength)
                {
                    // This shouldn't happen in any normal case. 
                    // We'd need either a) a very large number of individual parameters; 
                    // b) multiple parameters (not just the trigger) with large payloads.
                    // At this point, we can't log all that, so just truncate. 
                    this.Arguments = null;
                }
                else if (truncate)
                {
                    Dictionary<string, string> args2 = new Dictionary<string, string>();
                    foreach (var kv in this.Arguments)
                    {
                        args2[kv.Key] = Truncate(kv.Value, MaxParameterPayloadLength);
                    }
                    this.Arguments = args2;
                }
            }
        }

        private static string Truncate(string value, int maxLength)
        {
            if (value != null)
            {
                if (value.Length > maxLength)
                {
                    return value.Substring(0, maxLength) + "...";
                }
            }
            return value;
        }     
    }    
}