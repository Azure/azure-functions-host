// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Web.Mvc;

namespace Dashboard.Controllers
{
    public class MainController : Controller
    {
        [Route("")]
        [AcceptVerbs(HttpVerbs.Get | HttpVerbs.Head)]
        public ActionResult Index()
        {
            if (!Request.Url.GetLeftPart(UriPartial.Path).EndsWith("/"))
            {
                return RedirectPermanent(Request.Url.GetLeftPart(UriPartial.Path) + "/");
            }
            return View();
        }
	}
}
