// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Security.Cryptography;

namespace Azure.Functions.Host.Tests;

internal static class WorkerLinkTestData
{
    public static readonly string RuntimeAccessKey = Convert.ToBase64String(SHA256.HashData("test-worker-key"u8));
}
