using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace SpaBookingSystem.DataLayer;

public class SpaDbContextFactory : IDesignTimeDbContextFactory<SpaDbContext>
{
    public SpaDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<SpaDbContext>();

        var cs = "Server=.,1433;Database=SpaBookingSystemDb;User Id=sa;Password=01692455711@Ge;TrustServerCertificate=True;Encrypt=False";

        optionsBuilder.UseSqlServer(cs);
        return new SpaDbContext(optionsBuilder.Options);
    }
}
