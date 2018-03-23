// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Net.Http;

namespace Microsoft.Azure.WebJobs.Script
{
    /// <summary>
    /// This class holds a static instance for HttpClient for all to use.
    /// It also allows injecting in a new HttpClient for unit testing
    /// </summary>
    public static class HttpClientUtility
    {
        private static HttpClient _default = new HttpClient();
        private static HttpClient _instance;

        public static HttpClient Instance
        {
            get { return _instance ?? _default; }
            // Used for testing. You can update this with an HttpClient with a custom
            // HttpClientHandler with a custom SendAsync implementation.
            internal set { _instance = value; }
        }
    }
}
