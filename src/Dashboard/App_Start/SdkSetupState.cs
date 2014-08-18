// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

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
            get { return "AzureWebJobsDashboard"; }
        }

        public enum ConnectionStringStates
        {
            Missing,
            Invalid,
            Valid
        }
    }
}
