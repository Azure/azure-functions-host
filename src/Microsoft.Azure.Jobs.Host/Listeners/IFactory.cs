namespace Microsoft.Azure.Jobs.Host.Listeners
{
    internal interface IFactory<T>
    {
        T Create();
    }
}
