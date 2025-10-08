using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using CountryTelegramBot;
using System;

namespace CountryTelegramBot
{
    class ProgramTest
    {
        static void Main(string[] args)
        {
            TestDatabaseConnection();
        }
        
        static void TestDatabaseConnection()
        {
            try
            {
                var configuration = new ConfigurationBuilder()
                    .SetBasePath(Directory.GetCurrentDirectory())
                    .AddJsonFile("appsettings.json")
                    .AddJsonFile("appsettings.Development.json", optional: true)
                    .Build();

                var connectionString = configuration.GetSection("ConnectionStrings:DefaultConnection").Value;
                
                if (string.IsNullOrEmpty(connectionString))
                {
                    Console.WriteLine("Connection string is not configured.");
                    return;
                }

                var optionsBuilder = new DbContextOptionsBuilder<DbCountryContext>();
                optionsBuilder.UseMySql(connectionString, ServerVersion.AutoDetect(connectionString));

                using var context = new DbCountryContext(optionsBuilder.Options);
                var canConnect = context.Database.CanConnect();
                Console.WriteLine($"Database connection test: {(canConnect ? "SUCCESS" : "FAILED")}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Database connection test failed: {ex.Message}");
            }
        }
    }
}