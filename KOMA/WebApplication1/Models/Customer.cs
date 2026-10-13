using System.ComponentModel.DataAnnotations;

namespace KOMA.Models;

// Login details remain on ApplicationUser. UserId is both the profile key and foreign key.
public class Customer
{
    [Key]
    public string UserId { get; set; } = string.Empty;
    public ApplicationUser User { get; set; } = null!;
}
