namespace Microsoft.WindowsAzure.Jobs.Host.Protocols
{
    internal interface IPersistentQueue<T>
    {
        T Dequeue();

        void Enqueue(T message);

        void Delete(T message);
    }
}
