// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Linq;
using System.Net.Http;

namespace Microsoft.Azure.WebJobs.Script.ExtensionBundle
{
    internal static class ExtensionBundleHttpExtensions
    {
        // The X-Azure-Ref header is added by Azure Front Door for requests that traverse Front Door to the origin.
        // Documentation: https://learn.microsoft.com/en-us/azure/frontdoor/front-door-http-headers-protocol#from-the-front-door-to-the-backend
        // We capture and log this value (sanitized + length bounded) with extension bundle download failures
        // to enable end-to-end correlation and root cause analysis on the Front Door side when diagnosing
        // CDN/edge or networking issues impacting bundle retrieval.
        internal const string AzureRefHeaderName = "X-Azure-Ref";

        internal static bool TryGetAzureRef(this HttpResponseMessage response, out string azureRef)
        {
            ArgumentNullException.ThrowIfNull(response);

            try
            {
                if (response.Headers is not null &&
                    response.Headers.TryGetValues(AzureRefHeaderName, out var values))
                {
                    azureRef = values.FirstOrDefault();
                    return !string.IsNullOrEmpty(azureRef);
                }
            }
            catch (Exception)
            {
            }

            azureRef = null;
            return false;
        }
    }
}
