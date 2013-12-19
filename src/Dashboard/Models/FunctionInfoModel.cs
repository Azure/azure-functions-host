using System;
using Dashboard.Models.Protocol;

namespace Dashboard.Controllers
{
    // Show static information about the function 
    public class FunctionInfoModel
    {
        public FunctionDefinitionModel Descriptor { get; set; }

        public ParamModel[] Parameters { get; set; }

        // List of {name}
        public string[] KeyNames { get; set; }

        public Guid ReplayGuid { get; set; }

        // Purely cosmetic (we recompute this on the server for security)
        public string UploadContainerName { get; set; }
    }
}
