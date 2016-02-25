// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Net;
using System.Net.Http;
using System.Web.Http;

namespace WebJobs.Script.WebHost.Controllers
{
    public class HomeController : ApiController
    {
        public HttpResponseMessage Get()
        {
            // TODO: Eventually we'll want to consider returning a content
            // page
            return new HttpResponseMessage(HttpStatusCode.NoContent);
        }
    }
}
