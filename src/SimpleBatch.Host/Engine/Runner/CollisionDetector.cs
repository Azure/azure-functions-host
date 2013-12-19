namespace Microsoft.WindowsAzure.Jobs
{
    internal class CollisionDetector
    {
        // ### We don't have the Static binders...
        // Throw if binds read and write to the same resource. 
        public static void DetectCollisions(BindResult[] binds)
        {
        }
    }
}
