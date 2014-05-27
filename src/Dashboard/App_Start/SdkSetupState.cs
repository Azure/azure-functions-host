using Microsoft.Azure.Jobs;

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
            get { return JobHost.DashboardConnectionStringName; }
        }

        public enum ConnectionStringStates
        {
            Missing,
            Invalid,
            Valid
        }
    }
}
