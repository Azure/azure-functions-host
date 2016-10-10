// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Configuration;
using System.Web.Http;
using Autofac;
using Xunit;

namespace Dashboard.UnitTests
{
    public class AutoFacTests
    {
        private const string FunctionLogTableAppSettingName = "AzureWebJobsLogTableName";

        // Test everything is registered in both modes. 
        [Theory]
        [InlineData(FunctionLogTableAppSettingName, "logTestTable123")] // Use fast logging 
        [InlineData(FunctionLogTableAppSettingName, null)] // use classic SDK logging
        public void RegistrationTest(string appsetting, string value)
        {
            var oldSetting = ConfigurationManager.AppSettings[appsetting];
            try
            {
                ConfigurationManager.AppSettings[appsetting] = value;

                var container = MvcApplication.BuildContainer(new HttpConfiguration());

                // Verify we can create all the API & MVC controller classes. 
                // This is really testing that all dependencies are properly registered 
                container.Resolve<ApiControllers.DiagnosticsController>();
                container.Resolve<ApiControllers.FunctionsController>();
                container.Resolve<ApiControllers.LogController>();

                container.Resolve<Controllers.FunctionController>();
                container.Resolve<Controllers.MainController>();
            }
            finally
            {
                ConfigurationManager.AppSettings[appsetting] = oldSetting;
            }
        }    
    }
}