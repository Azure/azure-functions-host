// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Web.Mvc;

namespace Dashboard.Controllers
{
    public class MainController : Controller
    {
        [Route("")]
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
