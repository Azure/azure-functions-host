
using System.ComponentModel;

namespace WebJobs.Script.Cli
{
    internal enum Context
    {
        None,

        [Description("For Azure login and working with Function Apps on Azure")]
        Azure,

        [Description("For Azure account and subscriptions settings and actions")]
        Account,

        [Description("For local function app settings and actions")]
        FunctionApp,

        [Description("For Azure Storage settings and actions")]
        Storage,

        [Description("For local Functions host settings and actions")]
        Host,

        [Description("For local function settings and actions")]
        Function,

        [Description("For Azure account and subscriptions settings and actions")]
        Subscriptions,

        [Description("For local settings for your Functions host")]
        Settings
    }

    internal static class ContextEnumExtensions
    {
        public static string ToLowerCaseString(this Context context)
        {
            return context.ToString().ToLowerInvariant();
        }
    }
}
