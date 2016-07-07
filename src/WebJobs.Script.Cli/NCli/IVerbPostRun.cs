using System.Threading.Tasks;

namespace NCli
{
    public interface IVerbPostRun
    {
        Task PostRunVerbAsync();
    }
}
