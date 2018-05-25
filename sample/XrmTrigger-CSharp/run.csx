#r "../bin/Microsoft.Xrm.Sdk.dll"

using System;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Query;

public static void Run(IExecutionContext context, IOrganizationService service, TraceWriter log)
{
    log.Info("got service");
    string msg = $"[{DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss.fff")}]:  ****************** It works!!!";
    log.Info(msg);

    try
    {
        Entity e = new Entity("account");
        e["name"] = "test";

        var entity = service.Retrieve("account", Guid.NewGuid(), null);
        var request = new CreateRequest();
        request.Target = entity;
        var id = service.Execute(request);
        // log.Info($"Created entity with id={id}");
    }
    catch (Exception ex)
    {
        log.Error("Encountered error " + ex.ToString());
        throw;
    }
}