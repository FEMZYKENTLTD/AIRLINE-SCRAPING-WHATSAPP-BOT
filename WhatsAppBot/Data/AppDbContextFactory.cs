using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace WhatsAppBot.Data
{
    /// <summary>
    /// Design-time factory used by the EF Core CLI (dotnet-ef) to create a
    /// DbContext without running the application (Program.cs).
    /// Used in CI to validate that migrations match the current model.
    /// </summary>
    public class AppDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
    {
        public AppDbContext CreateDbContext(string[] args)
        {
            // Deterministic in-memory connection string: the CLI only needs the
            // relational metadata, it never touches a real database here.
            var options = new DbContextOptionsBuilder<AppDbContext>()
                .UseSqlite("DataSource=design-time-memory.db")
                .Options;

            return new AppDbContext(options);
        }
    }
}
