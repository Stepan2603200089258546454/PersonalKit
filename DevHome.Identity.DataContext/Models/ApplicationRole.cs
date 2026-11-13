using Microsoft.AspNetCore.Identity;

namespace DevHome.Identity.DataContext.Models
{
    public class ApplicationRole : IdentityRole
    {
        public ApplicationRole() : base()
        {
        }

        public ApplicationRole(string roleName) : base(roleName)
        {
        }
    }
}
