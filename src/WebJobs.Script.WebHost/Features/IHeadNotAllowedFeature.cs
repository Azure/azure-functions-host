// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

namespace Microsoft.Azure.WebJobs.Script.WebHost.Features;

internal interface IHeadNotAllowedFeature
{
    string AllowedMethods { get; }
}

internal sealed class HeadNotAllowedFeature : IHeadNotAllowedFeature
{
    public HeadNotAllowedFeature(string allowedMethods)
    {
        AllowedMethods = allowedMethods;
    }

    public string AllowedMethods { get; }
}
