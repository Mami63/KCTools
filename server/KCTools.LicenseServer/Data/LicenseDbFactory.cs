using System;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace KCTools.LicenseServer.Data
{
    /// <summary>
    /// `dotnet ef migrations add` için tasarım-zamanı bağlamı. Gerçek bağlantı
    /// gerekmez; migration üretimi çevrimdışı çalışır.
    /// </summary>
    public sealed class LicenseDbFactory : IDesignTimeDbContextFactory<LicenseDbContext>
    {
        public LicenseDbContext CreateDbContext(string[] args)
        {
            var connection =
                Environment.GetEnvironmentVariable("KCTOOLS_DB_CONNECTION")
                ?? "Host=localhost;Port=5432;Database=kctools;Username=postgres;Password=postgres";

            var options = new DbContextOptionsBuilder<LicenseDbContext>()
                .UseNpgsql(connection)
                .Options;

            return new LicenseDbContext(options);
        }
    }
}
