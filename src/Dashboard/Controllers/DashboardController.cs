using System.Collections.Generic;
using System.Linq;
using System.Web.Mvc;
using Dashboard.ViewModels;
using Microsoft.WindowsAzure.Jobs;

namespace Dashboard.Controllers
{
    public class DashboardController : Controller
    {
        private readonly Services _services;
        private readonly IFunctionTableLookup _functionTableLookup;
        private readonly IRunningHostTableReader _heartbeatTable;

        internal DashboardController(
            Services services, 
            IFunctionTableLookup functionTableLookup, 
            IRunningHostTableReader heartbeatTable)
        {
            _services = services;
            _functionTableLookup = functionTableLookup;
            _heartbeatTable = heartbeatTable;
        }

        public ActionResult Index()
        {
            var logger = _services.GetFunctionInstanceQuery();

            var model = new DashboardIndexViewModel();

            var query = new FunctionInstanceQueryFilter();
            model.InvocationLogViewModels = logger
                .GetRecent(10, query)
                .Select(x => new InvocationLogViewModel(x))
                .ToArray();

            var hearbeats = _heartbeatTable.ReadAll();
            model.FunctionStatisticsViewModels = _functionTableLookup
                .ReadAll()
                .Select(f => new FunctionStatisticsViewModel
                {
                    FunctionFullName = f.ToString(),
                    FunctionName = f.Location.GetShortName(),
                    IsRunning = HasValidHeartbeat(f, hearbeats),
                    FailedCount = 0,
                    SuccessCount = 0,
                    LastStartTime = f.Timestamp
                }).ToArray();

            var table = _services.GetInvokeStatsTable();
            
            var all = table.Enumerate();
            foreach (var item in all)
            {
                string rowKey = item["RowKey"];
                var func = _functionTableLookup.Lookup(rowKey);

                if (func == null)
                {
                    // ignore functions in stats but not found
                    continue;
                }

                var statsModel = model
                    .FunctionStatisticsViewModels
                    .FirstOrDefault(x => 
                        x.FunctionFullName == func.ToString()
                    );

                if (statsModel != null)
                {
                    var stats = ObjectBinderHelpers.ConvertDictToObject<FunctionStatsEntity>(item);
                    statsModel.FailedCount = stats.CountErrors;
                    statsModel.SuccessCount = stats.CountCompleted;
                }
            }

            return View(model);
        }

        public ActionResult About()
        {
            var model = new DashboardAboutViewModel();

            // Get health
            model.VersionInformation = FunctionInvokeRequest.CurrentSchema.ToString();
            model.QueueDepth = _services.GetExecutionQueueDepth();
            model.AccountName = _services.Account.Credentials.AccountName;

            return View(model);
        }

        public ActionResult PartialFunctionStatistics()
        {
            var hearbeats = _heartbeatTable.ReadAll();
            var model = _functionTableLookup
                .ReadAll()
                .Select(f => new FunctionStatisticsViewModel
                {
                    FunctionFullName = f.ToString(),
                    FunctionName = f.Location.GetShortName(),
                    IsRunning = HasValidHeartbeat(f, hearbeats),
                    FailedCount = 0,
                    SuccessCount = 0,
                    LastStartTime = f.Timestamp
                }).ToArray();

            var table = _services.GetInvokeStatsTable();

            var all = table.Enumerate();
            foreach (var item in all)
            {
                string rowKey = item["RowKey"];
                var func = _functionTableLookup.Lookup(rowKey);

                if (func == null)
                {
                    // ignore functions in stats but not found
                    continue;
                }

                var statsModel = model.FirstOrDefault(x =>
                        x.FunctionFullName == func.ToString()
                    );

                if (statsModel != null)
                {
                    var stats = ObjectBinderHelpers.ConvertDictToObject<FunctionStatsEntity>(item);
                    statsModel.FailedCount = stats.CountErrors;
                    statsModel.SuccessCount = stats.CountCompleted;
                }
            }

            return PartialView(model);
        }

        private static bool HasValidHeartbeat(FunctionDefinition func, IEnumerable<RunningHost> heartbeats)
        {
            string assemblyFullName = func.GetAssemblyFullName();
            RunningHost heartbeat = heartbeats.FirstOrDefault(h => h.AssemblyFullName == assemblyFullName);
            return RunningHost.IsValidHeartbeat(heartbeat);
        }
    }
}
