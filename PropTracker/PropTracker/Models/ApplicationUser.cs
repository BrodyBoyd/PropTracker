using Microsoft.AspNetCore.Identity;

namespace PropTracker.Models
{
    public class ApplicationUser : IdentityUser
    {
        public List<Prop> Props { get; set; } = new();
    }
}
