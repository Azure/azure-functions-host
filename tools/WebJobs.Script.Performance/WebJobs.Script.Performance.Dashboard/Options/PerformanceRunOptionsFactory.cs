using System.Threading.Tasks;

namespace WebJobs.Script.Tests.Perf.Dashboard
{
    public interface IPerformanceRunOptionsFactory
    {
        Task<PerformanceRunOptions> CreateAsync();
    }
}
