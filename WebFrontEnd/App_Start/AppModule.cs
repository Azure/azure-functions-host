using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Ninject;
using Ninject.Modules;
using WebFrontEnd.Configuration;

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
        }
    }
}
