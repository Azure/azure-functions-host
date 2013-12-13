using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;
using System.Web.Mvc;
using System.Web.Mvc.Html;
using System.Web.Routing;
using DaasEndpoints;
using Executor;
using Microsoft.WindowsAzure;
using Microsoft.WindowsAzure.StorageClient;
using Orchestrator;
using RunnerInterfaces;
using WebFrontEnd.Models.Protocol;

namespace WebFrontEnd.Controllers
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

        public static MvcHtmlString TimeLapse(
            this HtmlHelper htmlHelper,
            DateTime now, DateTime past)
        {
            var span = now - past;
            return TimeLapse(htmlHelper, span);
        }

        public static MvcHtmlString TimeLapse(
            this HtmlHelper htmlHelper,
            TimeSpan span)
        {
            string s;
            if (span.TotalSeconds < 60)
            {
                s = string.Format("{0:0.0}s ago", span.TotalSeconds);
            }
            else if (span.TotalSeconds < 60 * 60)
            {
                s = string.Format("{0}m {1:0.0}s ago", span.Minutes, span.Seconds);
            }
            else
            {
                s = string.Format("{0} ago", span);
            }
            return MvcHtmlString.Create(s);

        }

        // Emit an HTML link to the log for the given function instance.
        public static MvcHtmlString FunctionInstanceLogLinkVerbose(
            this HtmlHelper htmlHelper,
            ExecutionInstanceLogEntityModel log)
        {
            string name = log.Name;
            return FunctionInstanceLogLink(htmlHelper, log.FunctionInstance, name);
        }

        // This should be the common link, most friendly version. 
        // Emit an HTML link to the log for the given function instance.
        public static MvcHtmlString FunctionInstanceLogLink(
            this HtmlHelper htmlHelper,
            ExecutionInstanceLogEntityModel log)
        {
            return htmlHelper.Partial("_FunctionInstanceLogLink", log);      
        }

        public static MvcHtmlString FunctionInstanceLogLink(
            this HtmlHelper htmlHelper,
            FunctionInvokeRequestModel instance,
            string textLink = null)
        {
            // $$$ Is this used anywhere?
            if (textLink == null)
            {
                textLink = instance.ToString();
            }

            return LinkExtensions.ActionLink(
                htmlHelper,
                textLink,
                "FunctionInstance", "Log",
                new { func = instance.Id },
                null);
        }

        // Overload when we have a guid. Includes the resolve to resolve to a nice function name.
        public static MvcHtmlString FunctionInstanceLogLink(
            this HtmlHelper htmlHelper,
            Guid id,
            LogFunctionModel logFunctionModel)
        {
            ExecutionInstanceLogEntity log = logFunctionModel.Lookup.Lookup(id);
            if (log != null)
            {
                return FunctionInstanceLogLink(htmlHelper, new ExecutionInstanceLogEntityModel(log));
            }
            // No entry matching the guid. Just show the raw guid then. 
            return FunctionInstanceLogLink(htmlHelper, id);
        }

        // Overload when we only have a guid, no resolver. 
        public static MvcHtmlString FunctionInstanceLogLink(
            this HtmlHelper htmlHelper,
            Guid? id,
            string textLink = null)
        {
            if (!id.HasValue)
            {
                return MvcHtmlString.Empty;
            }
            if (textLink == null)
            {
                textLink = id.ToString();
            }

            return LinkExtensions.ActionLink(
                htmlHelper,
                textLink,
                "FunctionInstance", "Log",
                new { func = id.Value},
                null);
        }

         // Emit HTML link to the log for the function descriptor.        
         public static MvcHtmlString FunctionLogLink(this HtmlHelper htmlHelper,
            FunctionDefinitionModel func)
         {
             return LinkExtensions.ActionLink(
                 htmlHelper,
                func.Location.GetShorterName(),
                 "Index", "Function", 
                new { func = func.ToString() }, 
                 null);
         }

        public static string GetShorterName(this FunctionLocationModel self)
        {
            var shortName = self.UnderlyingObject.GetShortName();
            var lastDotIndex = shortName.LastIndexOf('.');
            if (lastDotIndex >= 0)
            {
                return shortName.Substring(lastDotIndex + 1);
            }
            return shortName;
        }

        public static MvcHtmlString FunctionFullNameLink(this HtmlHelper htmlHelper,
            FunctionDefinitionModel func)
        {
            return LinkExtensions.ActionLink(
                htmlHelper,
                func.Location.ToString(),
                "Index", "Function",
                new { func = func.ToString() },
                null);
        }

        // Lists the static information about the given function type.
        public static MvcHtmlString FunctionLogLink(this HtmlHelper htmlHelper,
            FunctionLocationModel func)
        {
            return LinkExtensions.ActionLink(
                htmlHelper,
                func.GetShorterName(),
                "Index", "Function",
                new { func = func.ToString() },
                null);
        }

        // Emit HTML link to history of a function. 
        // This can list all instances of that function 
        public static MvcHtmlString FunctionLogInvokeHistoryLink(this HtmlHelper htmlHelper,
            FunctionLocationModel func)
        {
            return FunctionLogInvokeHistoryLink(htmlHelper, func, null);
        }

        public static MvcHtmlString FunctionLogInvokeHistoryLink(this HtmlHelper htmlHelper,
            FunctionLocationModel func, string linkText, bool? success = null)
        {
            string msg = linkText ?? string.Format("{0} invoke history", func.UnderlyingObject.GetShortName());
            return LinkExtensions.ActionLink(
                htmlHelper,
                msg,
                "ListFunctionInstances", "Log",
                new { 
                    func = func.ToString(),
                    success = success
                },
                null);
        }


        // Emit link to page describing blob usage and histo
        public static MvcHtmlString BlobLogLink(this HtmlHelper htmlHelper,
            CloudBlobDescriptorModel blobPath)
        {
            return LinkExtensions.ActionLink(
                htmlHelper,
                linkText: blobPath.UnderlyingObject.GetId(),
                actionName: "Blob",
                routeValues: 
                new { 
                    path = new CloudBlobPath(blobPath.UnderlyingObject).ToString(),
                    accountName = blobPath.UnderlyingObject.GetAccount().Credentials.AccountName
                });
        }

        public static MvcHtmlString ReplayFunctionInstance(this HtmlHelper htmlHelper, ExecutionInstanceLogEntityModel log)
        {
            return LinkExtensions.ActionLink(
                htmlHelper,
                linkText: "Replay " + log.ToString(),
                actionName: "InvokeFunctionReplay", 
                controllerName: "Function",
                routeValues: new { instance =  log.GetKey() },
                htmlAttributes: null                
                );

        }

        // Renders a link to the console output for the given function instance.
        public static MvcHtmlString FunctionOutputLink(this HtmlHelper htmlHelper,
            ExecutionInstanceLogEntityModel log)
        {
            if (log.UnderlyingObject.OutputUrl == null)
            {
                return MvcHtmlString.Create("No console output available.");
            }
            TagBuilder builder = new TagBuilder("a");
            builder.MergeAttribute("href", log.UnderlyingObject.OutputUrl);
            builder.InnerHtml = "Console output";

            string html = builder.ToString(TagRenderMode.Normal);
            return MvcHtmlString.Create(html);
        }

        // Get an optional link for the parameter value
        public static MvcHtmlString ParamArgValueLink(this HtmlHelper htmlHelper, ParamModel p)
        {
            if (p.ArgBlobLink != null)
            {
                return LinkExtensions.ActionLink(
               htmlHelper,
               linkText: p.ArgInvokeString,
               actionName: "Blob",
               routeValues:
               new
               {
                   path = new CloudBlobPath(p.ArgBlobLink.UnderlyingObject).ToString(),
                   accountName = p.ArgBlobLink.UnderlyingObject.GetAccount().Credentials.AccountName
               });
            }
            return MvcHtmlString.Create(p.ArgInvokeString);
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
