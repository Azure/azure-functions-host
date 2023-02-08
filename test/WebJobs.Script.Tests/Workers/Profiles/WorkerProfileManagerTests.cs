// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Collections.Generic;
using Microsoft.Azure.WebJobs.Script.Tests.Workers.Rpc;
using Microsoft.Azure.WebJobs.Script.Workers;
using Microsoft.Azure.WebJobs.Script.Workers.Profiles;
using Microsoft.Azure.WebJobs.Script.Workers.Rpc;
using Newtonsoft.Json.Linq;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests.Workers.Profiles
{
    public class WorkerProfileManagerTests
    {
        private static TestLogger<WorkerProfileManager> _testLogger = new();
        private static TestEnvironment _testEnvironment = new();

        [Fact]
        public void LoadWorkerDescriptionFromProfiles_ConditionsMet_ReturnsDescriptionWithChanges()
        {
            var argumentListA = new string[] { "-TestArg=1" };
            var argumentListB = new string[] { "-TestArg=2" };
            var defaultDescription = RpcWorkerConfigTestUtilities.GetTestDefaultWorkerDescription("java", argumentListA);
            var profileDescription = RpcWorkerConfigTestUtilities.GetTestDefaultWorkerDescription("java", argumentListB);
            var profiles = WorkerDescriptionProfileData("java", profileDescription);

            _testEnvironment.SetEnvironmentVariable("APPLICATIONINSIGHTS_ENABLE_AGENT", "true");

            WorkerProfileManager profileManager = new(_testLogger, _testEnvironment);
            profileManager.SetWorkerDescriptionProfiles(profiles, "java");
            profileManager.LoadWorkerDescriptionFromProfiles(defaultDescription, out var evaluatedDescription);

            Assert.NotEqual(defaultDescription, evaluatedDescription);
        }

        [Fact]
        public void LoadWorkerDescriptionFromProfiles_NoConditionsMet_ReturnsDefaultDescription()
        {
            var argumentListA = new string[] { "-TestArg=1" };
            var argumentListB = new string[] { "-TestArg=2" };
            var defaultDescription = RpcWorkerConfigTestUtilities.GetTestDefaultWorkerDescription("java", argumentListA);
            var profileDescription = RpcWorkerConfigTestUtilities.GetTestDefaultWorkerDescription("java", argumentListB);
            var profiles = WorkerDescriptionProfileData("java", profileDescription);

            WorkerProfileManager profileManager = new(_testLogger, _testEnvironment);
            profileManager.SetWorkerDescriptionProfiles(profiles, "java");
            profileManager.LoadWorkerDescriptionFromProfiles(defaultDescription, out var evaluatedDescription);

            Assert.Equal(defaultDescription, evaluatedDescription);
        }

        [Fact]
        public void TryCreateWorkerProfileCondition_ValidCondition_ReturnsTrue()
        {
            var conditionJObject = new JObject();
            conditionJObject[WorkerConstants.WorkerDescriptionProfileConditionType] = "environment";
            conditionJObject[WorkerConstants.WorkerDescriptionProfileConditionName] = "PROFILE_TEST_ENVIRONMENT_VARIABLE";
            conditionJObject[WorkerConstants.WorkerDescriptionProfileConditionExpression] = "true";
            var conditionDescriptor = conditionJObject.ToObject<WorkerProfileConditionDescriptor>();

            WorkerProfileManager profileManager = new(_testLogger, _testEnvironment);
            var result = profileManager.TryCreateWorkerProfileCondition(conditionDescriptor, out var condition);

            Assert.True(result);
        }

        [Fact]
        public void TryCreateWorkerProfileCondition_InvalidCondition_ReturnsFalse()
        {
            var conditionJObject = new JObject();
            conditionJObject[WorkerConstants.WorkerDescriptionProfileConditionType] = "faketype";
            conditionJObject[WorkerConstants.WorkerDescriptionProfileConditionName] = "PROFILE_TEST_ENVIRONMENT_VARIABLE";
            conditionJObject[WorkerConstants.WorkerDescriptionProfileConditionExpression] = "true";
            var conditionDescriptor = conditionJObject.ToObject<WorkerProfileConditionDescriptor>();

            WorkerProfileManager profileManager = new(_testLogger, _testEnvironment);
            var result = profileManager.TryCreateWorkerProfileCondition(conditionDescriptor, out var condition);

            Assert.False(result);
        }

        [Fact]
        public void IsCorrectProfileLoaded_NoChange_ReturnsTrue()
        {
            var argumentList = new string[] { "-TestArg=1" };
            var description = RpcWorkerConfigTestUtilities.GetTestDefaultWorkerDescription("java", argumentList);
            var profiles = WorkerDescriptionProfileData("java", description);

            _testEnvironment.SetEnvironmentVariable("APPLICATIONINSIGHTS_ENABLE_AGENT", "true");

            WorkerProfileManager profileManager = new(_testLogger, _testEnvironment);
            profileManager.SetWorkerDescriptionProfiles(profiles, "java");
            profileManager.LoadWorkerDescriptionFromProfiles(description, out var workerDescription);

            // Same profile should load as we didn't change any condition outcomes
            Assert.True(profileManager.IsCorrectProfileLoaded("java"));
        }

        [Fact]
        public void IsCorrectProfileLoaded_ConditionChange_ReturnsFalse()
        {
            var argumentList = new string[] { "-TestArg=1" };
            var description = RpcWorkerConfigTestUtilities.GetTestDefaultWorkerDescription("java", argumentList);
            var profiles = WorkerDescriptionProfileData("java", description);

            _testEnvironment.SetEnvironmentVariable("APPLICATIONINSIGHTS_ENABLE_AGENT", "true");

            WorkerProfileManager profileManager = new(_testLogger, _testEnvironment);
            profileManager.SetWorkerDescriptionProfiles(profiles, "java");
            profileManager.LoadWorkerDescriptionFromProfiles(description, out var workerDescription);

            // Change env var so the condition will evaluate for a different profile
            _testEnvironment.SetEnvironmentVariable("APPLICATIONINSIGHTS_ENABLE_AGENT", "false");

            Assert.False(profileManager.IsCorrectProfileLoaded("java"));
        }

        [Fact]
        public void IsCorrectProfileLoaded_DifferentRuntimeNoProfile_ReturnsTrue()
        {
            var argumentList = new string[] { "-TestArg=1" };
            var description = RpcWorkerConfigTestUtilities.GetTestDefaultWorkerDescription("java", argumentList);
            var profiles = WorkerDescriptionProfileData("java", description);

            _testEnvironment.SetEnvironmentVariable("APPLICATIONINSIGHTS_ENABLE_AGENT", "true");

            WorkerProfileManager profileManager = new(_testLogger, _testEnvironment);
            profileManager.SetWorkerDescriptionProfiles(profiles, "java");
            profileManager.LoadWorkerDescriptionFromProfiles(description, out var workerDescription);

            // Same profile should load as we didn't change any condition outcomes
            Assert.True(profileManager.IsCorrectProfileLoaded("java"));

            // Different runtime without profiles
            Assert.True(profileManager.IsCorrectProfileLoaded("dotnet"));
        }

        [Fact]
        public void IsCorrectProfileLoaded_DifferentRuntimeWithProfile_ReturnsTrue()
        {
            var argumentList = new string[] { "-TestArg=1" };
            var description = RpcWorkerConfigTestUtilities.GetTestDefaultWorkerDescription("java", argumentList);
            var profiles = WorkerDescriptionProfileData("java", description);

            _testEnvironment.SetEnvironmentVariable("APPLICATIONINSIGHTS_ENABLE_AGENT", "true");

            WorkerProfileManager profileManager = new(_testLogger, _testEnvironment);
            profileManager.SetWorkerDescriptionProfiles(profiles, "java");
            profileManager.LoadWorkerDescriptionFromProfiles(description, out var javaWorkerDescription);

            var dotnetArgumentList = new string[] { "-TestArg=2" };
            var dotnetDescription = RpcWorkerConfigTestUtilities.GetTestDefaultWorkerDescription("dotnet", dotnetArgumentList);
            var dotnetProfiles = WorkerDescriptionProfileData("dotnet", dotnetDescription);

            profileManager.SetWorkerDescriptionProfiles(dotnetProfiles, "dotnet");
            profileManager.LoadWorkerDescriptionFromProfiles(dotnetDescription, out var dotnetWorkerDescription);

            // Same profile should load as we didn't change any condition outcomes
            Assert.True(profileManager.IsCorrectProfileLoaded("java"));

            // Different runtime also loads same profile as we didn't change any condition outcomes
            Assert.True(profileManager.IsCorrectProfileLoaded("dotnet"));
        }

        [Fact]
        public void IsCorrectProfileLoaded_DifferentRuntimeWithProfile_ReturnsFalse()
        {
            var argumentList = new string[] { "-TestArg=1" };
            var description = RpcWorkerConfigTestUtilities.GetTestDefaultWorkerDescription("java", argumentList);
            var profiles = WorkerDescriptionProfileData("java", description);

            _testEnvironment.SetEnvironmentVariable("APPLICATIONINSIGHTS_ENABLE_AGENT", "true");

            WorkerProfileManager profileManager = new(_testLogger, _testEnvironment);
            profileManager.SetWorkerDescriptionProfiles(profiles, "java");
            profileManager.LoadWorkerDescriptionFromProfiles(description, out var javaWorkerDescription);

            var dotnetArgumentList = new string[] { "-TestArg=2" };
            var dotnetDescription = RpcWorkerConfigTestUtilities.GetTestDefaultWorkerDescription("dotnet", dotnetArgumentList);
            var dotnetProfiles = WorkerDescriptionProfileData("dotnet", dotnetDescription);

            profileManager.SetWorkerDescriptionProfiles(dotnetProfiles, "dotnet");
            profileManager.LoadWorkerDescriptionFromProfiles(dotnetDescription, out var dotnetWorkerDescription);

            // Same profile should load as we didn't change any condition outcomes
            Assert.True(profileManager.IsCorrectProfileLoaded("java"));

            // Changing the condition so a different profile evaluates to true
            _testEnvironment.SetEnvironmentVariable("APPLICATIONINSIGHTS_ENABLE_AGENT", "false");
            Assert.False(profileManager.IsCorrectProfileLoaded("dotnet"));
        }

        public static List<WorkerDescriptionProfile> WorkerDescriptionProfileData(string language, RpcWorkerDescription description)
        {
            var conditionLogger = new TestLogger("ConditionLogger");
            var profilesList = new List<WorkerDescriptionProfile>();

            var conditionListA = new List<IWorkerProfileCondition>();
            conditionListA.Add(ProfilesTestUtilities.GetTestEnvironmentCondition(conditionLogger, _testEnvironment, "APPLICATIONINSIGHTS_ENABLE_AGENT", "true"));
            var profileA = new WorkerDescriptionProfile("profileA", conditionListA, description);

            var conditionListB = new List<IWorkerProfileCondition>();
            conditionListB.Add(ProfilesTestUtilities.GetTestEnvironmentCondition(conditionLogger, _testEnvironment, "APPLICATIONINSIGHTS_ENABLE_AGENT", "false"));
            var profileB = new WorkerDescriptionProfile("profileB", conditionListB, description);

            profilesList.Add(profileA);
            profilesList.Add(profileB);

            return profilesList;
        }

        public void Dispose()
        {
            _testEnvironment.Clear();
        }
    }
}