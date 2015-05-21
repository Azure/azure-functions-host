// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Web.Mvc;

namespace Dashboard.Filters
{
    internal sealed class AccountContextAttribute : FilterAttribute, IResultFilter
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
            if (filterContext == null)
            {
                throw new ArgumentNullException("filterContext");
            }

            filterContext.Controller.ViewBag.DashboardHasSetupError = _context.HasSetupError;
            filterContext.Controller.ViewBag.DashboardConnectionStringName = 
                DashboardAccountContext.PrefixedConnectionStringName;
            filterContext.Controller.ViewBag.DashboardStorageAccountName = _context.SdkStorageAccountName;
            filterContext.Controller.ViewBag.DashboardConnectionStringState = _context.ConnectionStringState;
        }
    }
}