// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;

namespace Microsoft.Azure.WebJobs.Script.WebHost.Extensions
{
    public static class VfsStringExtensions
    {
        public static string EscapeHashCharacter(this string str)
        {
            return str.Replace("#", Uri.EscapeDataString("#"));
        }
    }
}