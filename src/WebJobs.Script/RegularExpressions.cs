// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Text.RegularExpressions;

namespace Microsoft.Azure.WebJobs.Script
{
    internal partial class RegularExpressions
    {
        [GeneratedRegex(@"{(.*?)\}", RegexOptions.IgnoreCase)]
        internal static partial Regex GetFunctionBindingAttributeExpressionRegex();
    }
}
