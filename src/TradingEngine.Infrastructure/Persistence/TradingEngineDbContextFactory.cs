using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace TradingEngine.Infrastructure.Persistence;

public sealed class TradingEngineDbContextFactory : IDesignTimeDbContextFactory<TradingEngineDbContext>
{
    public TradingEngineDbContext CreateDbContext(string[] args)
    {
        string connectionString =
            Environment.GetEnvironmentVariable("ConnectionStrings__TradingEngine")
            ?? "Server=(localdb)\\mssqllocaldb;Database=TradingEngine;Trusted_Connection=True;Encrypt=False";

        DbContextOptionsBuilder<TradingEngineDbContext> options = new();
        options.UseSqlServer(connectionString);

        return new TradingEngineDbContext(options.Options);
    }
}
