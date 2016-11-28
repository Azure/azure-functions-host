// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using Microsoft.WindowsAzure.Storage.Table;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Globalization;

namespace Microsoft.Azure.WebJobs.Logging
{
    // Capture each individual function instance. 
    // This has full fidelity to the FunctionInstanceLogItem and can rehydrate it. 
    // 1 entity per Function-Instance. 
    internal class InstanceTableEntity : TableEntity, IEntityWithEpoch, IFunctionInstanceBaseEntry
    {
        const string PartitionKeyFormat = TableScheme.InstancePK;
        const string RowKeyFormat = "{0}"; // functionInstanceId

        internal static InstanceTableEntity New(FunctionInstanceLogItem item)
        {
            item.Truncate();

            string argumentJson = null;
            if (item.Arguments != null)
            {
                argumentJson = JsonConvert.SerializeObject(item.Arguments);
            }

            return new InstanceTableEntity
            {
                PartitionKey = PartitionKeyFormat,
                RowKey = RowKeyFunctionInstanceId(item.FunctionInstanceId),

                ParentId = item.ParentId,
                FunctionName = item.FunctionName,
                TriggerReason = item.TriggerReason,
                StartTime = item.StartTime,
                FunctionInstanceHeartbeatExpiry = item.FunctionInstanceHeartbeatExpiry,
                EndTime = item.EndTime,

                LogOutput = item.LogOutput,

                ErrorDetails = item.ErrorDetails,
                ArgumentsJson = argumentJson
            };
        }

        public FunctionInstanceLogItem ToFunctionLogItem()
        {
            return new FunctionInstanceLogItem
            {
                FunctionInstanceId = ((IFunctionInstanceBaseEntry) this).FunctionInstanceId,
                ParentId =  this.ParentId,
                FunctionName = this.FunctionName,
                TriggerReason = this.TriggerReason,
                StartTime = this.StartTime,
                EndTime = this.EndTime,
                FunctionInstanceHeartbeatExpiry = this.FunctionInstanceHeartbeatExpiry,
                ErrorDetails = this.ErrorDetails,
                LogOutput = this.LogOutput,
                Arguments = this.GetArguments()
            };
        }

        public DateTime GetEpoch()
        {
            return this.StartTime; 
        }

        internal static TableOperation GetRetrieveOperation(Guid functionInstanceId)
        {
            // Create a retrieve operation that takes a customer entity.
            TableOperation retrieveOperation = TableOperation.Retrieve<InstanceTableEntity>(
                PartitionKeyFormat,
                RowKeyFunctionInstanceId(functionInstanceId));
            return retrieveOperation;
        }

        // InstancePK
        // entity per function instance 
        internal static string RowKeyFunctionInstanceId(Guid functionInstanceId)
        {
            return string.Format(CultureInfo.InvariantCulture, RowKeyFormat, functionInstanceId);
        }
                
        // Properties from log. 
        public string FunctionName { get; set; }

        public string TriggerReason { get; set; }

        public DateTime StartTime { get; set; }

        public DateTime? EndTime { get; set; }

        // Last heart beat. When EndTime is missing, this lets us guess if the function is still alive. 
        // Whereas Start and End are written very determinsitically in the lifecycle, 
        // heartbeat is special because it's written at potentially random times by a background poller thread. 
        public DateTime? FunctionInstanceHeartbeatExpiry { get; set; }

        public string ErrorDetails { get; set; }

        // Direct inline capture for small log outputs. For large log outputs, this is faulted over to a blob. 
        /// <summary></summary>
        public string LogOutput { get; set; }

        // Json encoded argument dictionary
        public string ArgumentsJson { get; set; }

        private IDictionary<string, string> GetArguments()
        {
            if (this.ArgumentsJson == null)
            {
                return new Dictionary<string, string>();
            }
            return JsonConvert.DeserializeObject<IDictionary<string, string>>(this.ArgumentsJson);
        }

        public Guid? ParentId { get; private set; }

        Guid IFunctionInstanceBaseEntry.FunctionInstanceId
        {
            get
            {
                return Guid.Parse(this.RowKey);
            }
        }

        bool IFunctionInstanceBaseEntry.HasError
        {
            get
            {
                return this.ErrorDetails != null;
            }
        }
    }
}