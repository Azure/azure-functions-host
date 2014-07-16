// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.Linq;
using System.Web;
using System.Web.Mvc;
using System.Web.Routing;

namespace Dashboard.Controllers
{
    // HTML helpers for emitting links and things for various interfaces.
    // Another benefit to HTML helpers is that is that the IDE doesn't find property references in CSHTML.
    public static class MoreHtmlHelpers
    {
        /// <summary>
        /// Renders an &lt;li&gt; tag containing a "Menu Item Link" which is a standard link that displays a marker indicating if its target is the current action.
        /// </summary>
        /// <param name="icon">The CSS class for an icon to use for the menu item</param>
        /// <param name="name">The text to display for the link</param>
        public static HtmlString MenuLink(this HtmlHelper self, string name, string icon, string action, string controller)
        {
            return MenuLink(self, name, icon, action, controller, null);
        }

        /// <summary>
        /// Renders an &lt;li&gt; tag containing a "Menu Item Link" which is a standard link that displays a marker indicating if its target is the current action.
        /// </summary>
        /// <param name="icon">The CSS class for an icon to use for the menu item</param>
        /// <param name="name">The text to display for the link</param>
        public static HtmlString MenuLink(this HtmlHelper self, string name, string icon, string action, string controller, object routeValues)
        {
            var url = new UrlHelper(self.ViewContext.RequestContext);
            RouteValueDictionary target;
            if (routeValues != null)
            {
                target = new RouteValueDictionary(routeValues);
            }
            else
            {
                target = new RouteValueDictionary();
            }
            target["action"] = action;
            target["controller"] = controller;

            var li = new TagBuilder("li");
            var a = new TagBuilder("a");
            var i = new TagBuilder("i");
            if (SameRoute(target, self.ViewContext.RouteData.Values))
            {
                li.AddCssClass("active");
            }
            i.AddCssClass(icon);
            a.Attributes["href"] = url.RouteUrl(target);
            a.InnerHtml = i.ToString(TagRenderMode.Normal) + " " + name;
            li.InnerHtml = a.ToString(TagRenderMode.Normal);
            return new HtmlString(li.ToString(TagRenderMode.Normal));
        }

        /// <summary>
        /// Helper to check if two RouteValueDictionaries have the same values
        /// </summary>
        private static bool SameRoute(RouteValueDictionary target, RouteValueDictionary routeValueDictionary)
        {
            var matches = target
                .Where(pair => routeValueDictionary.ContainsKey(pair.Key) && Equals(pair.Value, routeValueDictionary[pair.Key]))
                .Select(pair => pair.Key);
            var mismatches = routeValueDictionary.Keys.Except(matches);
            return !mismatches.Any();
        }
    }
}
