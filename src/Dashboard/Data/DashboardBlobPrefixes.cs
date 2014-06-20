namespace Dashboard.Data
{
    // Names of directories used only by the dashboard (not part of the protocol with the host).
    internal static class DashboardBlobPrefixes
    {
        private const string ByFunction = "by-function/";

        public const string Flat = "flat/";

        public static string CreateByFunctionPrefix(string functionId)
        {
            return ByFunction + functionId + "/";
        }
    }
}
