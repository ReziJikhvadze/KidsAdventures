namespace AdventurePacks.Api.Data;

public interface ISqlConnectionFactory
{
    IDbConnection CreateConnection();
}

public sealed class SqlConnectionFactory(IConfiguration configuration) : ISqlConnectionFactory
{
    private readonly string _connectionString = configuration.GetConnectionString("DefaultConnection")
                                              ?? throw new InvalidOperationException("Connection string 'DefaultConnection' is missing.");

    public IDbConnection CreateConnection() => new SqlConnection(_connectionString);
}
