using System;
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


        // Renders a relative DateTime string representation
        public static string DateTimeRelative(this HtmlHelper htmlHelper, DateTime dateTimeUtc)
        {
            return DateTimeRelative(dateTimeUtc);
        }
        // Renders a relative TimeSpan string representation
        public static string TimeSpanRelative(this HtmlHelper htmlHelper, TimeSpan timeSpan)
        {
            return TimeSpanRelative(timeSpan);
        }

        public static string DateTimeRelative(DateTime dateTimeUtc)
        {
            var time = DateTime.UtcNow - dateTimeUtc;

            if (time.TotalDays > 7 || time.TotalDays < -7)
                return ConvertToTimeZone(dateTimeUtc).ToString("'on' MMM d yyyy 'at' h:mm tt");

            if (time.TotalHours > 24)
                return Plural("1 day ago", "{0} days ago", time.Days);
            if (time.TotalHours < -24)
                return Plural("in 1 day", "in {0} days", -time.Days);

            if (time.TotalMinutes > 60)
                return Plural("1 hour ago", "{0} hours ago", time.Hours);
            if (time.TotalMinutes < -60)
                return Plural("in 1 hour", "in {0} hours", -time.Hours);

            if (time.TotalSeconds > 60)
                return Plural("1 minute ago", "{0} minutes ago", time.Minutes);
            if (time.TotalSeconds < -60)
                return Plural("in 1 minute", "in {0} minutes", -time.Minutes);

            if (time.TotalSeconds > 10)
                return Plural("1 second ago", "{0} seconds ago", time.Seconds); //aware that the singular won't be used
            if (time.TotalSeconds < -10)
                return Plural("in 1 second", "in {0} seconds", -time.Seconds);

            return time.TotalMilliseconds > 0
                       ? "a moment ago"
                       : "in a moment";
        }

        public static string TimeSpanRelative(TimeSpan timeSpan)
        {
            if (timeSpan.TotalDays > 7)
                return timeSpan.ToString(Plural("1 week", "{0} weeks", timeSpan.Days));

            if (timeSpan.TotalHours > 24)
                return Plural("1 day", "{0} days", timeSpan.Days);

            if (timeSpan.TotalMinutes > 60)
                return Plural("1 hour", "{0} hours", timeSpan.Hours);

            if (timeSpan.TotalSeconds > 60)
                return Plural("1 minute", "{0} minutes", timeSpan.Minutes);

            if (timeSpan.TotalSeconds > 10)
                return Plural("1 second", "{0} seconds", timeSpan.Seconds);

            if (timeSpan.TotalMilliseconds > 1000)
                return Plural("1 s", "{0} s", timeSpan.Seconds);

            if (timeSpan.TotalMilliseconds > 0)
                return Plural("1 ms", "{0} ms", (int)timeSpan.TotalMilliseconds);

            return "less than 1ms";
        }

        private static string Plural(string singular, string plural, int count, params object[] args)
        {
            return String.Format(count == 1 ? singular : plural, new object[] { count }.Concat(args).ToArray());
        }

        private static DateTime ConvertToTimeZone(DateTime dateTimeUtc)
        {
            // using UTC as the default time zone for displaying times
            // this code is useless for now
            var timeZone = TimeZoneInfo.Utc;
            return TimeZoneInfo.ConvertTimeFromUtc(dateTimeUtc, timeZone);
        }
    }
}
