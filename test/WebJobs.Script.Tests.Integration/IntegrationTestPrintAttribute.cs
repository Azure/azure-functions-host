// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Reflection;
using Xunit.Sdk;

namespace Microsoft.Azure.WebJobs.Script.Tests.Integration;

public sealed class IntegrationTestPrintAttribute : BeforeAfterTestAttribute
{
    public override void Before(MethodInfo methodUnderTest)
    {
        string testName = GetTestName(methodUnderTest);
        IntegrationTestPrintLogger.TestStart(testName);
    }

    public override void After(MethodInfo methodUnderTest)
    {
        string testName = GetTestName(methodUnderTest);
        IntegrationTestPrintLogger.TestEnd(testName);
    }

    private static string GetTestName(MethodInfo methodUnderTest)
    {
        string className = methodUnderTest?.DeclaringType?.FullName ?? "UnknownClass";
        string methodName = methodUnderTest?.Name ?? "UnknownMethod";
        return $"{className}.{methodName}";
    }
}
