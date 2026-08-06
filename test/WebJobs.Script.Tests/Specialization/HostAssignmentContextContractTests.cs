// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Azure.WebJobs.Script.WebHost.Models;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests.Specialization;

public class HostAssignmentContextContractTests : EnvironmentContractTestBase
{
    [Fact]
    public void ApplyAppSettings_WritesPayloadCorsEasyAuthAndSiteUpdateSequentially()
    {
        List<string> operations = [];
        ConfigureEnvironment(record: operations.Add);
        HostAssignmentContext context = new()
        {
            Environment = new Dictionary<string, string>
            {
                ["payload-setting"] = "payload-value",
            },
            CorsSettings = new CorsSettings
            {
                AllowedOrigins = ["https://example.test"],
                SupportCredentials = true,
            },
            EasyAuthSettings = new EasyAuthSettings
            {
                SiteAuthEnabled = true,
                SiteAuthClientId = "site-client-id",
            },
            SiteUpdateId = 42,
        };

        context.ApplyAppSettings(_testEnvironment, NullLogger.Instance);

        Assert.Equal(
            [
                "write:payload-setting=payload-value",
                $"write:{EnvironmentSettingNames.CorsSupportCredentials}=True",
                $"write:{EnvironmentSettingNames.CorsAllowedOrigins}=[\"https://example.test\"]",
                $"read:{EnvironmentSettingNames.EasyAuthEnabled}",
                $"write:{EnvironmentSettingNames.EasyAuthEnabled}=True",
                $"write:{EnvironmentSettingNames.EasyAuthClientId}=site-client-id",
                $"write:{EnvironmentSettingNames.FunctionsSiteUpdateId}=42",
            ],
            operations);
    }

    [Theory]
    [InlineData(null, false, null, "True")]
    [InlineData("", false, null, "True")]
    [InlineData(" ", false, null, " ")]
    [InlineData("False", false, null, "False")]
    [InlineData("pre-existing", true, "payload", "payload")]
    [InlineData("pre-existing", true, "", "True")]
    [InlineData("pre-existing", true, null, "True")]
    public void ApplyAppSettings_EasyAuthEnabledUsesLiveValueAfterPayload(
        string preExistingValue, bool includePayload, string payloadValue, string expected)
    {
        Dictionary<string, string> values = new(StringComparer.Ordinal);
        if (preExistingValue is not null)
        {
            values[EnvironmentSettingNames.EasyAuthEnabled] = preExistingValue;
        }

        HostAssignmentContext context = new()
        {
            Environment = new Dictionary<string, string>(StringComparer.Ordinal),
            EasyAuthSettings = new EasyAuthSettings
            {
                SiteAuthEnabled = true,
                SiteAuthClientId = "site-client-id",
            },
            SiteUpdateId = 1,
        };
        if (includePayload)
        {
            context.Environment[EnvironmentSettingNames.EasyAuthEnabled] = payloadValue;
        }

        ConfigureEnvironment(values);
        context.ApplyAppSettings(_testEnvironment, NullLogger.Instance);

        Assert.Equal(expected, _testEnvironment.GetEnvironmentVariable(EnvironmentSettingNames.EasyAuthEnabled));
    }

    [Fact]
    public void ApplyAppSettings_EasyAuthClientIdNullIsAnUnconditionalDelete()
    {
        Dictionary<string, string> values = new(StringComparer.Ordinal)
        {
            [EnvironmentSettingNames.EasyAuthEnabled] = bool.FalseString,
            [EnvironmentSettingNames.EasyAuthClientId] = "old-client-id",
        };
        RecordingProcessMutator mutator = ConfigureEnvironment(values);
        HostAssignmentContext context = new()
        {
            Environment = new Dictionary<string, string>(),
            EasyAuthSettings = new EasyAuthSettings
            {
                SiteAuthEnabled = true,
                SiteAuthClientId = null,
            },
            SiteUpdateId = 2,
        };

        context.ApplyAppSettings(_testEnvironment, NullLogger.Instance);

        Assert.Null(_testEnvironment.GetEnvironmentVariable(EnvironmentSettingNames.EasyAuthClientId));
        Assert.Contains(
            mutator.Attempts,
            mutation => string.Equals(
                    mutation.Name,
                    EnvironmentSettingNames.EasyAuthClientId,
                    StringComparison.Ordinal)
                && mutation.Value is null);
    }

    [Fact]
    public void ApplyAppSettings_PreservesEmptyPayloadValuesAndNullDeletion()
    {
        const string emptyKey = "empty-setting";
        const string deletedKey = "deleted-setting";
        Dictionary<string, string> values = new(StringComparer.Ordinal)
        {
            [deletedKey] = "delete-me",
        };
        RecordingProcessMutator mutator = ConfigureEnvironment(values);
        HostAssignmentContext context = new()
        {
            Environment = new Dictionary<string, string>
            {
                [emptyKey] = string.Empty,
                [deletedKey] = null,
            },
            SiteUpdateId = 3,
        };

        context.ApplyAppSettings(_testEnvironment, NullLogger.Instance);

        Assert.Equal(string.Empty, _testEnvironment.GetEnvironmentVariable(emptyKey));
        Assert.Null(_testEnvironment.GetEnvironmentVariable(deletedKey));
        Assert.Equal(
            [emptyKey, deletedKey, EnvironmentSettingNames.FunctionsSiteUpdateId],
            mutator.Attempts.Select(mutation => mutation.Name));
    }

    [Fact]
    public void ApplyAppSettings_TestEnvironmentNullForMissingPayloadRetainsNullEntry()
    {
        const string nullKey = "missing-null-setting";
        ConfigureEnvironment();
        HostAssignmentContext context = new()
        {
            Environment = new Dictionary<string, string>
            {
                [nullKey] = null,
            },
            SiteUpdateId = 4,
        };

        context.ApplyAppSettings(_testEnvironment, NullLogger.Instance);

        Assert.True(_testEnvironment.ContainsEnvironmentVariable(nullKey));
        Assert.Null(_testEnvironment.GetEnvironmentVariable(nullKey));
    }

    private RecordingProcessMutator ConfigureEnvironment(
        IDictionary<string, string> values = null,
        Action<string> record = null)
    {
        _testEnvironment.Clear();
        if (values is not null)
        {
            foreach (KeyValuePair<string, string> pair in values)
            {
                _testEnvironment.SetEnvironmentVariable(pair.Key, pair.Value);
            }
        }

        RecordingProcessMutator mutator = new(record);
        _testEnvironment.OnGetEnvironmentVariable =
            name => record?.Invoke($"read:{name}");
        _testEnvironment.OnSetEnvironmentVariable = mutator.Set;

        return mutator;
    }
}
