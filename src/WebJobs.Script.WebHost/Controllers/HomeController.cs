// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Web.Http;
using Microsoft.Azure.WebJobs.Script.WebHost.Properties;

namespace Microsoft.Azure.WebJobs.Script.WebHost.Controllers
{
    public class HomeController : ApiController
    {
        public static bool IsHomepageDisabled
        {
            get
            {
                return string.Equals(Environment.GetEnvironmentVariable(EnvironmentSettingNames.AzureWebJobsDisableHomepage),
                    bool.TrueString, StringComparison.OrdinalIgnoreCase);
            }
        }

        public HttpResponseMessage Get()
        {
            return IsHomepageDisabled
                ? new HttpResponseMessage(HttpStatusCode.NoContent)
                : new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(Resources.Homepage, Encoding.UTF8, "text/html")
                };
        }
    }
}
