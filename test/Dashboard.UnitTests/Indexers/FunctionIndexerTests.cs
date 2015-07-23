// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using Dashboard.Data;
using Dashboard.Indexers;
using Microsoft.Azure.WebJobs.Protocols;
using Xunit;

namespace Dashboard.UnitTests.Indexers
{
    public class FunctionIndexerTests
    {
        [Fact]
        public void CreateSnapshot_CreatesExpectedSnapshot()
        {
            FunctionDescriptor function = new FunctionDescriptor
            {
                Id = "FunctionId",
                FullName = "FullName",
                ShortName = "ShortName",
                Parameters = new ParameterDescriptor[] 
                { 
                    new ParameterDescriptor { Name = "param1" },
                    new ParameterDescriptor { Name = "param2" }
                }
            };

            FunctionStartedMessage message = new FunctionStartedMessage
            {
                FunctionInstanceId = Guid.NewGuid(),
                HostInstanceId = Guid.NewGuid(),
                InstanceQueueName = "InstanceQueueName",
                Reason = ExecutionReason.AutomaticTrigger,
                ReasonDetails = "A trigger fired!",
                Heartbeat = new HeartbeatDescriptor
                {
                    InstanceBlobName = "InstanceBlobName",
                    SharedContainerName = "SharedContainerName",
                    SharedDirectoryName = "SharedDirectoryName",
                    ExpirationInSeconds = 5
                },
                SharedQueueName = "SharedQueueName",
                Function = function,
                Arguments = new Dictionary<string, string> 
                {
                    { "param1", "foo" },
                    { "param2", "bar" }
                },
                ParentId = Guid.NewGuid(),
                StartTime = DateTime.Now,
                OutputBlob = new LocalBlobDescriptor { BlobName = "OutputBlobName", ContainerName = "OutputBlobContainerName" },
                ParameterLogBlob = new LocalBlobDescriptor { BlobName = "ParameterLogBlobName", ContainerName = "ParameterLogBlobContainerName" },
                WebJobRunIdentifier = new WebJobRunIdentifier { JobName = "JobName", JobType = WebJobTypes.Triggered, RunId = "RunId", WebSiteName = "WebSiteName" }
            };

            FunctionInstanceSnapshot snapshot = FunctionIndexer.CreateSnapshot(message);

            Assert.Equal(message.FunctionInstanceId, snapshot.Id);
            Assert.Equal(message.HostInstanceId, snapshot.HostInstanceId);
            Assert.Equal(message.InstanceQueueName, snapshot.InstanceQueueName);
            Assert.Same(message.Heartbeat, snapshot.Heartbeat);
            Assert.Equal("SharedQueueName_FunctionId", snapshot.FunctionId);
            Assert.Equal(message.Function.FullName, snapshot.FunctionFullName);
            Assert.Equal(message.Function.ShortName, snapshot.FunctionShortName);
            Assert.Equal(2, snapshot.Arguments.Count);
            Assert.Equal("foo", snapshot.Arguments["param1"].Value);
            Assert.Equal("bar", snapshot.Arguments["param2"].Value);
            Assert.Equal(message.ParentId, snapshot.ParentId);
            Assert.Equal(message.ReasonDetails, snapshot.Reason);
            Assert.Equal(message.StartTime, snapshot.QueueTime);
            Assert.Equal(message.StartTime, snapshot.StartTime);
            Assert.Equal(message.OutputBlob, snapshot.OutputBlob);
            Assert.Same(message.ParameterLogBlob, snapshot.ParameterLogBlob);
            Assert.Equal(message.WebJobRunIdentifier.WebSiteName, snapshot.WebSiteName);
            Assert.Equal(message.WebJobRunIdentifier.JobType.ToString(), snapshot.WebJobType);
            Assert.Equal(message.WebJobRunIdentifier.JobName, snapshot.WebJobName);
            Assert.Equal(message.WebJobRunIdentifier.RunId, snapshot.WebJobRunId);
        }

        [Theory]
        //test string with newline starting at char 19, which causes \r to remain in the final DisplayTitle string
        [InlineData("XXXXXX\r\nXXXXXXXXX\r\nYYYYYYY", " (XXXXXXXXXXXXXXXYYY ...)")]
        [InlineData("{\r\n  \"QRPoint\": {\r\n    \"X\": 0,\r\n    \"Y\": 0\r\n  },\r\n  \"TimesheetId\": 0,\r\n  \"HasReadableQrCode\": false,\r\n  \"HasSignature\": false,\r\n  \"BlobAddress\": \"https://myaccount.blob.core.windows.net/container/file.png\",\r\n  \"$AzureWebJobsParentId\": \"511f605d-14bf-46ca-b321-96b59d9e81d6\"\r\n}", " ({  \"QRPoint\": {    ...)")]
        [InlineData("\r\n\r\n", " ()")]
        [InlineData("", " ()")]
        [InlineData("XXXXXXXXXXXXXXXXXXXXXXXXXXXXXX", " (XXXXXXXXXXXXXXXXXX ...)")]
        [InlineData("XXXXXX", " (XXXXXX)")]
        public void FunctionInstanceSnapshot_BuildDisplayTitle_CompletelyRemovesLineBreaks(string argumentValue, string expectedDisplayTitle)
        {
            FunctionInstanceSnapshot message = new FunctionInstanceSnapshot
            {
                Arguments = new Dictionary<string, FunctionInstanceArgument> 
                {
                    { "message", new FunctionInstanceArgument() { Value = argumentValue } }
                },
            };

            string displayTitle = message.DisplayTitle;

            Assert.Equal(expectedDisplayTitle, displayTitle);
            Assert.DoesNotContain("\r", displayTitle);
            Assert.DoesNotContain("\n", displayTitle);        
        }
    }
}
