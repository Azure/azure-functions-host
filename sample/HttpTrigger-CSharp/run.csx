#r "../bin/Microsoft.Xrm.Sdk.dll"

using System;
using Microsoft.Xrm.Sdk;

 public static void Run(IExecutionContext context, IOrganizationService service, TraceWriter log)
{
    log.Info("got service");
    string msg = $"[{DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss.fff")}]:  ****************** It works!!!";
    log.Info(msg);

    try
    {
        Entity e = new Entity("account");
        e["name"] = "test";

        service.Create(e);
        log.Info("Created entity");
    }
    catch (Exception ex)
    {
        log.Error("Encountered error " + ex.ToString());
        throw;
    }
}