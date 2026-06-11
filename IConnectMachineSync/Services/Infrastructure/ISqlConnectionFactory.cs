using Microsoft.Data.SqlClient;

namespace IConnectMachineSync.Services.Infrastructure;

public interface ISqlConnectionFactory
{
    SqlConnection CreateConnection();
}
