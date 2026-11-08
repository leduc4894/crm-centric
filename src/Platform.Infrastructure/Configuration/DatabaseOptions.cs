using Npgsql;

namespace Platform.Infrastructure.Configuration;

public sealed record DatabaseOptions(string ConnectionString)
{
    public override string ToString() => nameof(DatabaseOptions);

    public static DatabaseOptions Parse(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) throw new ArgumentException("Database configuration required.");
        var builder = new NpgsqlConnectionStringBuilder(value);
        if (string.IsNullOrWhiteSpace(builder.Host) || string.IsNullOrWhiteSpace(builder.Database)
            || string.IsNullOrWhiteSpace(builder.Username))
            throw new ArgumentException("Database configuration incomplete.");
        return new DatabaseOptions(builder.ConnectionString);
    }
}
