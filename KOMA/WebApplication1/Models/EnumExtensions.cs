using System.ComponentModel.DataAnnotations;
using System.Reflection;

namespace KOMA.Models
{
    public static class EnumExtensions
    {
        // Returns the [Display(Name = "...")] text if there is one, e.g. ReadyToServe -> "Ready to Serve".
        public static string DisplayName(this Enum value)
        {
            var member = value.GetType().GetMember(value.ToString()).FirstOrDefault();
            return member?.GetCustomAttribute<DisplayAttribute>()?.GetName() ?? value.ToString();
        }
    }
}
