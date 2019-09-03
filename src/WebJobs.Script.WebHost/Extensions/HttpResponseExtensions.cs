// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Net.Http;
using System.Net.Http.Headers;
using Microsoft.AspNetCore.Http;

namespace Microsoft.AspNetCore.Http
{
    public static class HttpResponseExtensions
    {
        public static bool HasStarted(this HttpResponse response)
        {
            // TODO - remove the need for this
            // in some cases HttpResponse.HasStarted isn't returning the correct
            // value. This extension is a workaround.
            // TODO - https://github.com/Azure/azure-functions-host/issues/4875
            if (response.HasStarted || response.ContentLength > 0 || (response.Body != null && response.Body.Length > 0))
            {
                return true;
            }

            return false;
        }
    }
}