using Microsoft.Data.SqlClient;

namespace IConnectMachineSync.Services.Infrastructure;

public sealed class SqlConnectionFactory(IConfiguration configuration) : ISqlConnectionFactory
{
    public SqlConnection CreateConnection()
    {
        var connectionString = configuration.GetConnectionString("Connection");
        if (string.IsNullOrWhiteSpace(connectionString))
            throw new InvalidOperationException("ConnectionStrings:Connection is required.");

        return new SqlConnection(connectionString);
    }
}
