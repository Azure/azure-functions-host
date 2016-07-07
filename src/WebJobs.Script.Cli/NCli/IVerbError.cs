using System;
using System.Threading.Tasks;

namespace NCli
{
    public interface IVerbError
    {
        Task OnErrorAsync(Exception exception);
    }
}
