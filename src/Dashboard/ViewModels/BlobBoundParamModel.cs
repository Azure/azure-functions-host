using System;

namespace Dashboard.ViewModels
{
    public class BlobBoundParamModel
    {
        public bool IsBlobOwnedByCurrentFunctionInstance { get; set; }
        public bool IsBlobMissing { get; set; }
        public Guid OwnerId { get; set; }
        public bool IsOutput { get; set; }
    }
}
