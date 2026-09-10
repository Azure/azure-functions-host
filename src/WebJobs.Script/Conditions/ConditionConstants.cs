// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

namespace Microsoft.Azure.WebJobs.Script.Conditions
{
    /// <summary>
    /// JSON field names and condition-type identifiers used by <see cref="ConditionDescriptor"/>
    /// and condition providers.
    /// </summary>
    public static class ConditionConstants
    {
        public const string ConditionType = "conditionType";
        public const string ConditionName = "conditionName";
        public const string ConditionExpression = "conditionExpression";

        public const string EnvironmentConditionType = "environment";
        public const string HostPropertyConditionType = "hostProperty";
    }
}
