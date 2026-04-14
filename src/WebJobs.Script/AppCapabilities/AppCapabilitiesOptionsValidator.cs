// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Collections.Generic;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace Microsoft.Azure.WebJobs.Script.AppCapabilities
{
    internal sealed class AppCapabilitiesOptionsValidator : IValidateOptions<AppCapabilitiesOptions>
    {
        // Size-based: matches existing Antares-enforced limit for sync triggers API
        private const int MaxCapabilitiesSizeBytes = ScriptConstants.MaxTriggersStringLength; // 204800

        public ValidateOptionsResult Validate(string name, AppCapabilitiesOptions options)
        {
            var serialized = JsonSerializer.Serialize(
                (IDictionary<string, string>)options,
                DictionaryJsonContext.Default.IDictionaryStringString);

            var sizeBytes = Encoding.UTF8.GetByteCount(serialized);

            if (sizeBytes > MaxCapabilitiesSizeBytes)
            {
                return ValidateOptionsResult.Fail(
                    $"Capabilities size ({sizeBytes} bytes) exceeds maximum ({MaxCapabilitiesSizeBytes} bytes).");
            }

            return ValidateOptionsResult.Success;
        }
    }
}
