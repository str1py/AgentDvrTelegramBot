using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;

namespace CountryTelegramBot
{
    public class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<DbCountryContext>
    {
        public DbCountryContext CreateDbContext(string[] args)
        {
            // Get the directory where the executable is located
            var basePath = Directory.GetCurrentDirectory();
            
            // Build configuration
            IConfiguration configuration = new ConfigurationBuilder()
                .SetBasePath(basePath)
                .AddJsonFile("appsettings.json")
                .AddJsonFile("appsettings.Development.json", optional: true)
                .Build();

            var connectionString = configuration.GetSection("ConnectionStrings:DefaultConnection").Value;
            
            if (string.IsNullOrEmpty(connectionString))
            {
                throw new InvalidOperationException("Connection string is not configured.");
            }

            var optionsBuilder = new DbContextOptionsBuilder<DbCountryContext>();
            optionsBuilder.UseMySql(connectionString, ServerVersion.AutoDetect(connectionString));

            return new DbCountryContext(optionsBuilder.Options);
        }
    }
}