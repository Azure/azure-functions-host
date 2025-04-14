// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Text.Json;

namespace Microsoft.Azure.WebJobs.Script.DependencyInjection
{
    /// <summary>
    /// Provides shared <see cref="JsonSerializerOptions"/> instances for consistent JSON serialization behavior.
    /// </summary>
    public static class JsonSerializerOptionsProvider
    {
        /// <summary>
        /// A preconfigured <see cref="JsonSerializerOptions"/> instance with case-insensitive property name matching.
        /// </summary>
        public static readonly JsonSerializerOptions CaseInsensitiveOptions = new()
        {
            PropertyNameCaseInsensitive = true
        };
    }
}
