#if PUBLICPROTOCOL
namespace Microsoft.Azure.Jobs.Protocols
#else
namespace Microsoft.Azure.Jobs.Host.Protocols
#endif
{
    internal static class WebSitesKnownKeyNames
    {
        public const string WebSiteNameKey = "WEBSITE_SITE_NAME";
        public const string JobDataPath = "WEBJOBS_DATA_PATH";
        public const string JobNameKey = "WEBJOBS_NAME";
        public const string JobTypeKey = "WEBJOBS_TYPE";
        public const string JobRunIdKey = "WEBJOBS_RUN_ID";
    }
}
