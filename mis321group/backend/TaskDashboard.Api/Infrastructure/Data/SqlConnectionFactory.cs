using MySqlConnector;

namespace TaskDashboard.Api.Infrastructure.Data;

public sealed class SqlConnectionFactory(IConfiguration configuration)
{
    private readonly string _connectionString =
        configuration.GetConnectionString("MySql")
        ?? "server=localhost;port=3306;database=taskdashboard;user=root;password=changeme;";

    public MySqlConnection CreateConnection()
    {
        return new MySqlConnection(_connectionString);
    }
}
