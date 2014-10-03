// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.Web.Mvc;

namespace Dashboard.Filters
{
    internal class AccountContextAttribute : FilterAttribute, IResultFilter
    {
        private readonly DashboardAccountContext _context;

        public AccountContextAttribute(DashboardAccountContext context)
        {
            _context = context;
        }

        public void OnResultExecuted(ResultExecutedContext filterContext)
        {
        }

        public void OnResultExecuting(ResultExecutingContext filterContext)
        {
            filterContext.Controller.ViewBag.DashboardHasSetupError = _context.HasSetupError;
            filterContext.Controller.ViewBag.DashboardConnectionStringName = 
                DashboardAccountContext.PrefixedConnectionStringName;
            filterContext.Controller.ViewBag.DashboardStorageAccountName = _context.SdkStorageAccountName;
            filterContext.Controller.ViewBag.DashboardConnectionStringState = _context.ConnectionStringState;
        }
    }
}