using Microsoft.Azure.Jobs;
using Microsoft.Azure.Jobs.Host.Runners;

namespace Dashboard
{
    public class SdkSetupState
    {
        public static string BadInitErrorMessage { get; internal set; }

        public static ConnectionStringStates ConnectionStringState { get; set; }

        public static bool BadInit
        {
            get { return ConnectionStringState != ConnectionStringStates.Valid; }
        }

        public static string DashboardConnectionStringName
        {
            get { return AmbientConnectionStringProvider.GetPrefixedConnectionStringName(JobHost.DashboardConnectionStringName); }
        }

        public enum ConnectionStringStates
        {
            Missing,
            Invalid,
            Valid
        }
    }
}
