using DevHome.Identity.DataContext.Models;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace DevHome.Identity.DataContext.Context
{
    public class DBContext(DbContextOptions<DBContext> options) : IdentityDbContext<ApplicationUser, ApplicationRole, string>(options)
    {
    }
}
