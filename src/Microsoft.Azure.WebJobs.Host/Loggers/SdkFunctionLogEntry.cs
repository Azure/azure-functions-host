using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Microsoft.Azure.WebJobs.Host.Loggers
{
    /// <summary>    
    /// Represent a function invocation starting or finishing. 
    /// Host can recieve these notifications to do their own logging. 
    /// </summary>
    public class SdkFunctionLogEntry
    {
        /// <summary>Gets or sets the function instance ID.</summary>
        public Guid FunctionInstanceId { get; set; }

        /// <summary>The parent instance that caused this function instance to run. this is used to establish causality between functions. </summary>
        public Guid? ParentId { get; set; }

        /// <summary>The name of the function. This serves as an identifier.</summary>
        public string FunctionName { get; set; } 

        /// <summary>The time the function started executing.</summary>
        public DateTime StartTime { get; set; }

        /// <summary>An optional value for when the function finished executing. If not set, then the function hasn't completed yet. </summary>
        public DateTime? EndTime { get; set; }

        /// <summary>
        /// Null on success.
        /// Else, set to some string with error details. 
        /// </summary>
        public string ErrorDetails { get; set; }

        /// <summary>Gets or sets the function's argument values and help strings.</summary>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Usage", "CA2227:CollectionPropertiesShouldBeReadOnly")]
        public IDictionary<string, string> Arguments { get; set; }

        // Direct inline capture for small log outputs. For large log outputs, this is faulted over to a blob. 
        /// <summary></summary>
        public string LogOutput { get; set; }

        /// <summary>
        /// Maximum length of LogOutput that will be captured. 
        /// </summary>
        public const int MaxLogLength = 1000;
    }
}

