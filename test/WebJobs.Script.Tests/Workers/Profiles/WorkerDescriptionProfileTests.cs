// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using Microsoft.Azure.WebJobs.Script.Tests.Workers.Rpc;
using Microsoft.Azure.WebJobs.Script.Workers;
using Microsoft.Azure.WebJobs.Script.Workers.Profiles;
using Microsoft.Azure.WebJobs.Script.Workers.Rpc;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests.Workers.Profiles
{
    public class WorkerDescriptionProfileTests
    {
        private static TestEnvironment _testEnvironment = new TestEnvironment();
        private static TestSystemRuntimeInformation _testSystemRuntimeInfo = new TestSystemRuntimeInformation();
        private static TestLogger _testLogger = new TestLogger("test");

        private static string[] argumentList = new string[] { "-TestArg=1" };

        [Theory]
        [MemberData(nameof(WorkerDescriptionProfileExceptionData))]
        public void WorkerDescriptionProfile_ThrowsValidationException(string name, List<IWorkerProfileCondition> conditions, RpcWorkerDescription workerDescription)
        {
            Assert.Throws<ValidationException>(() => new WorkerDescriptionProfile(name, conditions, workerDescription));
        }

        public static IEnumerable<object[]> WorkerDescriptionProfileExceptionData()
        {
            var description = RpcWorkerConfigTestUtilities.GetTestDefaultWorkerDescription("java", argumentList);

            var validConditionsList = new List<IWorkerProfileCondition>();
            validConditionsList.Add(ProfilesTestUtilities.GetTestEnvironmentCondition(_testLogger, _testEnvironment, "APPLICATIONINSIGHTS_ENABLE_AGENT", "true"));
            validConditionsList.Add(ProfilesTestUtilities.GetTestHostPropertyCondition(_testLogger, _testSystemRuntimeInfo, "hostversion", "4.*"));

            yield return new object[] { null, validConditionsList,  description };
            yield return new object[] { string.Empty, validConditionsList, description };
            yield return new object[] { "profileName", new List<IWorkerProfileCondition>(0), description };
            yield return new object[] { "profileName", null, description };
            yield return new object[] { "profileName", new List<IWorkerProfileCondition>(1), description };
        }

        [Theory]
        [MemberData(nameof(WorkerDescriptionProfileData))]
        public void WorkerDescriptionProfile_ApplyProfile(string name, List<IWorkerProfileCondition> conditions, RpcWorkerDescription workerDescription)
        {
            _testEnvironment.SetEnvironmentVariable("APPLICATIONINSIGHTS_ENABLE_AGENT", "true");
            var defaultDescription = RpcWorkerConfigTestUtilities.GetTestDefaultWorkerDescription("java", new string[] { "-DefaultArgs" });

            var workerDescriptionProfile = new WorkerDescriptionProfile(name, conditions, workerDescription);
            defaultDescription = workerDescriptionProfile.ApplyProfile(defaultDescription);

            Assert.Equal(defaultDescription.Arguments[0], argumentList[0]);
            Assert.NotEqual(defaultDescription.Arguments[0], "-DefaultArgs");

            // Reset environment
            _testEnvironment = new TestEnvironment();
        }

        public static IEnumerable<object[]> WorkerDescriptionProfileData()
        {
            var description = RpcWorkerConfigTestUtilities.GetTestDefaultWorkerDescription("java", argumentList);

            var validConditionsList = new List<IWorkerProfileCondition>();
            validConditionsList.Add(ProfilesTestUtilities.GetTestEnvironmentCondition(_testLogger, _testEnvironment, "APPLICATIONINSIGHTS_ENABLE_AGENT", "true"));
            yield return new object[] { "profileName", validConditionsList, description };

            validConditionsList.Add(ProfilesTestUtilities.GetTestHostPropertyCondition(_testLogger, _testSystemRuntimeInfo, "hostversion", "4.*"));
            yield return new object[] { "profileName", validConditionsList, description };
        }

        [Theory]
        [MemberData(nameof(WorkerDescriptionProfileInvalidData))]
        public void WorkerDescriptionProfile_DoNotApplyProfile(string name, List<IWorkerProfileCondition> conditions, RpcWorkerDescription workerDescription)
        {
            _testEnvironment.SetEnvironmentVariable("APPLICATIONINSIGHTS_ENABLE_AGENT", "false");
            var defaultDescription = RpcWorkerConfigTestUtilities.GetTestDefaultWorkerDescription("java", new string[] { "-DefaultArgs" });

            var workerDescriptionProfile = new WorkerDescriptionProfile(name, conditions, workerDescription);
            defaultDescription = workerDescriptionProfile.ApplyProfile(defaultDescription);

            Assert.NotNull(defaultDescription.Arguments[0]);
            Assert.Equal(defaultDescription.Arguments[0], "-DefaultArgs");

            // Reset environment
            _testEnvironment = new TestEnvironment();
        }

        public static IEnumerable<object[]> WorkerDescriptionProfileInvalidData()
        {
            var description = RpcWorkerConfigTestUtilities.GetTestDefaultWorkerDescription("java", new string[] { });

            var validConditionsList = new List<IWorkerProfileCondition>();
            validConditionsList.Add(ProfilesTestUtilities.GetTestEnvironmentCondition(_testLogger, _testEnvironment, "APPLICATIONINSIGHTS_ENABLE_AGENT", "true"));
            yield return new object[] { "profileName", validConditionsList, description };

            validConditionsList.Add(ProfilesTestUtilities.GetTestHostPropertyCondition(_testLogger, _testSystemRuntimeInfo, "hostversion", "3.*"));
            yield return new object[] { "profileName", validConditionsList, description };
        }

        [Fact]
        public void WorkerDescriptionProfile_EvaluateConditions()
        {
            var description = RpcWorkerConfigTestUtilities.GetTestDefaultWorkerDescription("java", argumentList);

            var conditions = new List<IWorkerProfileCondition>();
            conditions.Add(ProfilesTestUtilities.GetTestEnvironmentCondition(_testLogger, _testEnvironment, "APPLICATIONINSIGHTS_ENABLE_AGENT", "true"));

            var workerDescriptionProfile = new WorkerDescriptionProfile("profileName", conditions, description);
            Assert.False(workerDescriptionProfile.EvaluateConditions());

            _testEnvironment.SetEnvironmentVariable("APPLICATIONINSIGHTS_ENABLE_AGENT", "true");
            Assert.True(workerDescriptionProfile.EvaluateConditions());

            conditions.Add(ProfilesTestUtilities.GetTestHostPropertyCondition(_testLogger, _testSystemRuntimeInfo, "hostversion", "4.*"));
            workerDescriptionProfile = new WorkerDescriptionProfile("profileName", conditions, description);
            Assert.True(workerDescriptionProfile.EvaluateConditions());

            conditions.Add(ProfilesTestUtilities.GetTestHostPropertyCondition(_testLogger, _testSystemRuntimeInfo, "platform", "windows"));
            workerDescriptionProfile = new WorkerDescriptionProfile("profileName", conditions, description);

            Assert.False(workerDescriptionProfile.EvaluateConditions());

            // Reset environment
            _testEnvironment = new TestEnvironment();
        }
    }
}