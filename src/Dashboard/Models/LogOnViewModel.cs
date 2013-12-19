using System.ComponentModel.DataAnnotations;

namespace Dashboard.Controllers
{
    public class LogOnViewModel
    {
        [Required]
        [MinLength(3)]
        [MaxLength(15)] // sanity check
        [RegularExpression("[a-zA-Z0-9]+")] // especially useful since we use UserName in filename lookup
        public string UserName { get; set; }

        [Required]
        [DataType(DataType.Password)]
        [MaxLength(15)] // sanity check
        public string Password { get; set; }
    }
}
