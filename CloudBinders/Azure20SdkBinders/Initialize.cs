using SimpleBatch;

namespace Azure20SdkBinders
{
    public static class Initialize
    {
        public static void Add(IConfiguration config)
        {
            config.Binders.Add(new Azure20SdkBinderProvider());
            config.BlobBinders.Add(new BlobBinderProvider());
        }
    }
}