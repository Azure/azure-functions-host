// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Collections.Generic;

namespace Microsoft.Azure.WebJobs.Script.WebHost.Configuration
{
    /// <summary>
    /// Gets or sets the list of headers to add to every HTTP response.
    /// </summary>
    public class CustomHttpHeadersOptions : Dictionary<string, string>
    {
    }
}
