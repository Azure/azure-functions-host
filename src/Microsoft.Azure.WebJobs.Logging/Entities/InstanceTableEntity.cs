// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using Microsoft.WindowsAzure.Storage.Table;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;

namespace Microsoft.Azure.WebJobs.Logging
{
    // Capture each individual function instance. 
    // 1 entity per Function-Instance. 
    public class InstanceTableEntity : TableEntity
    {
        const string PartitionKeyFormat = TableScheme.InstancePK;
        const string RowKeyFormat = "{0}"; // functionInstanceId

        public static InstanceTableEntity New(FunctionLogItem item)
        {
            string argumentJson = null;
            if (item.Arguments != null)
            {
                argumentJson = JsonConvert.SerializeObject(item.Arguments);
            } 

            return new InstanceTableEntity
            {
                PartitionKey = PartitionKeyFormat,
                RowKey = RowKeyFunctionInstanceId(item.FunctionInstanceId),

                FunctionName = item.FunctionName,
                StartTime = item.StartTime,
                EndTime = item.EndTime,

                LogOutput = item.LogOutput,

                ErrorDetails = item.ErrorDetails,
                ArgumentsJson = argumentJson
            };
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
            return string.Format(RowKeyFormat, functionInstanceId);
        }

        public Guid GetFunctionInstanceId()
        {
            return Guid.Parse(this.RowKey);
        }

        // Properties from log. 
        public string FunctionName { get; set; }

        public DateTimeOffset StartTime { get; set; }

        public DateTimeOffset? EndTime { get; set; }

        public string ErrorDetails { get; set; }

        public bool IsSucceeded()
        {
            return this.ErrorDetails == null;
        }

        // Direct inline capture for small log outputs. For large log outputs, this is faulted over to a blob. 
        /// <summary></summary>
        public string LogOutput { get; set; }

        // Json encoded argument dictionary
        public string ArgumentsJson { get; set; }

        public IDictionary<string, string> GetArguments()
        {
            if (this.ArgumentsJson == null)
            {
                this.ArgumentsJson = "{}";
            }
            return JsonConvert.DeserializeObject<IDictionary<string, string>>(this.ArgumentsJson);

        }
    }
}