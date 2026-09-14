// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Text.Json;
using Microsoft.Azure.WebJobs.Script.Conditions;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;

namespace Microsoft.Azure.WebJobs.Script.Tests.Workers.Profiles
{
    public class ProfilesTestUtilities
    {
        public static JObject GetTestWorkerProfileCondition(string type = ConditionConstants.HostPropertyConditionType, string name = "hostVersion", string expression = "4.*")
        {
            var condition = new JObject();
            condition[ConditionConstants.ConditionType] = type;
            condition[ConditionConstants.ConditionName] = name;
            condition[ConditionConstants.ConditionExpression] = expression;
            return condition;
        }

        public static ConditionDescriptor GetTestConditionDescriptor(string type, string name, string expression)
        {
            var condition = GetTestWorkerProfileCondition(type, name, expression);
            return condition.ToObject<ConditionDescriptor>();
        }

        public static EnvironmentCondition GetTestEnvironmentCondition(ILogger logger, TestEnvironment testEnvironment, string name, string expression)
        {
            var descriptor = new ConditionDescriptor { Type = ConditionConstants.EnvironmentConditionType };
            descriptor.Properties[ConditionConstants.ConditionName] = JsonSerializer.SerializeToElement(name);
            descriptor.Properties[ConditionConstants.ConditionExpression] = JsonSerializer.SerializeToElement(expression);

            return new EnvironmentCondition(logger, testEnvironment, descriptor);
        }

        public static HostPropertyCondition GetTestHostPropertyCondition(ILogger logger, TestSystemRuntimeInformation testSystemRuntimeInfo, string name, string expression)
        {
            var descriptor = new ConditionDescriptor { Type = ConditionConstants.HostPropertyConditionType };
            descriptor.Properties[ConditionConstants.ConditionName] = JsonSerializer.SerializeToElement(name);
            descriptor.Properties[ConditionConstants.ConditionExpression] = JsonSerializer.SerializeToElement(expression);

            return new HostPropertyCondition(logger, testSystemRuntimeInfo, descriptor);
        }
    }
}
