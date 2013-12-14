using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;

#if false // ###
namespace Microsoft.WindowsAzure.Jobs.Dashboard
{
    // Entry point invoked for Azure.
    // http://blog.liamcavanagh.com/2011/12/how-to-combine-a-worker-role-with-a-mvc4-web-role-into-a-single-instance
    public class WebWorker : Microsoft.WindowsAzure.ServiceRuntime.RoleEntryPoint
    {
        public override bool OnStart()
        {
            return base.OnStart();
        }
    }
}
#endif