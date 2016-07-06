using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace WebJobs.Script.ConsoleHost.Arm
{
    public static class Utils
    {
        public static async Task SafeGuard(Func<Task> action)
        {
            try
            {
                await action();
            }
            catch (Exception e)
            {
                System.Diagnostics.Trace.TraceError($"SafeGuard Exception: {e.ToString()}");
            }
        }
        public static async Task<T> SafeGuard<T>(Func<Task<T>> action)
        {
            try
            {
                return await action();
            }
            catch (Exception e)
            {
                System.Diagnostics.Trace.TraceError($"SafeGuard<T> Exception: {e.ToString()}");
                return default(T);
            }
        }

        public static async Task Retry(Func<Task> func, int retryCount)
        {
            while (true)
            {
                try
                {
                    await func();
                    return;
                }
                catch (Exception e)
                {
                    if (retryCount <= 0) throw e;
                    retryCount--;
                }
                await Task.Delay(1000);
            }
        }

        public static async Task<T> Retry<T>(Func<Task<T>> func, int retryCount)
        {
            while (true)
            {
                try
                {
                    return await func();
                }
                catch (Exception e)
                {
                    if (retryCount <= 0) throw e;
                    retryCount--;
                }
                await Task.Delay(1000);
            }
        }

        public static async Task<T> Retry<T>(Func<Task<T>> func, Action<T> validate, Func<T, bool> retry, int maxRetryCount = -1)
        {
            T result = default(T);
            while (true)
            {
                try
                {
                    result = await func();
                    validate(result);
                    return result;
                }
                catch
                {
                    if (!retry(result) || (maxRetryCount != -1 && --maxRetryCount == 0)) throw;
                }
                await Task.Delay(1000);
            }
        }
    }
}
