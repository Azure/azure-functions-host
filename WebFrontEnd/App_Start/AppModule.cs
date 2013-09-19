using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Ninject;
using Ninject.Modules;
using WebFrontEnd.Configuration;
using DaasEndpoints;
using RunnerInterfaces;

namespace WebFrontEnd
{
    public class AppModule : NinjectModule
    {
        public override void Load()
        {
            // Bind Configuration services
            Bind<ConfigurationService>().ToSelf();
            Bind<AppConfiguration>()
                .ToMethod(context => context.Kernel.Get<ConfigurationService>().Current)
                .InSingletonScope();

            Services services = GetServices(this.Kernel);
            Bind<Services>().ToConstant(services);

            // $$$ This list should eventually just cover all of Services, and then we can remove services.
            Bind<IFunctionInstanceLookup>().ToConstant(services.GetFunctionInstanceLookup());
            Bind<IFunctionTable>().ToConstant(services.GetFunctionTable());
        }

        // Get a Services object based on current configuration.
        // $$$ Really should just get rid of this object and use DI all the way through. 
        static Services GetServices(IKernel kernel)
        {
            var config = kernel.Get<ConfigurationService>();

            AccountInfo accountInfo = new AccountInfo 
            {
                 AccountConnectionString = config.ReadSetting("MainStorage"),
                  WebDashboardUri = config.ReadSetting("WebRoleEndpoint")
            };
            return new Services(accountInfo);
        }
    }
}
