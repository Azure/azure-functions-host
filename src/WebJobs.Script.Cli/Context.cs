
using System.ComponentModel;

namespace WebJobs.Script.Cli
{
    enum Context
    {
        None,
        [Description("For Azure account and function apps running on azure")]
        Azure,
        [Description("For Azure account and subscriptions settings and actions")]
        Account,
        [Description("For function app settings and actions")]
        FunctionApp,
        [Description("For storage settings and actions")]
        Storage,
        [Description("For function runtime host settings and actions")]
        Host,
        [Description("For functions settings and actions")]
        Function,
        [Description("For Azure account and subscriptions settings and actions")]
        Subscriptions,
        [Description("For local settings for your function host")]
        Settings
    }
}
