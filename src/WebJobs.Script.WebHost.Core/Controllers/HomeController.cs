// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.IO;
using System.Text;
using Microsoft.AspNetCore.Mvc;

namespace Microsoft.Azure.WebJobs.Script.WebHost.Controllers
{
    public class HomeController : Controller
    {
        public static bool IsHomepageDisabled
        {
            get
            {
                return string.Equals(Environment.GetEnvironmentVariable(EnvironmentSettingNames.AzureWebJobsDisableHomepage),
                    bool.TrueString, StringComparison.OrdinalIgnoreCase);
            }
        }

        public IActionResult Get()
        {
            if (IsHomepageDisabled)
            {
                return NoContent();
            }

            return Content(GetHomepage(), "text/html", Encoding.UTF8);
        }

        private string GetHomepage()
        {
            var assembly = typeof(HomeController).Assembly;
            using (Stream resource = assembly.GetManifestResourceStream(assembly.GetName().Name + ".Home.html"))
            using (var reader = new StreamReader(resource))
            {
                return reader.ReadToEnd();
            }
        }
    }
}
