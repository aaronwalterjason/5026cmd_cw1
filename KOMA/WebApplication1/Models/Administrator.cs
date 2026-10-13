using System.ComponentModel.DataAnnotations;

namespace KOMA.Models;

public class Administrator
{
    [Key]
    public string UserId { get; set; } = string.Empty;
    public ApplicationUser User { get; set; } = null!;
}
