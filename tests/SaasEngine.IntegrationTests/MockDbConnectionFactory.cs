using System;
using System.Data;
using Dapper;
using Microsoft.Data.Sqlite;
using SaasEngine.Domain.Shared;

namespace SaasEngine.IntegrationTests;

/// <summary>
/// Mock SQLite connection factory for integration testing.
/// Uses a unique shared-cache in-memory database per factory instance to isolate test classes.
/// </summary>
public sealed class MockDbConnectionFactory : IAdminConnectionFactory, IDbConnectionFactory, IDisposable
{
    private readonly SqliteConnection _masterConnection;
    private readonly string _connectionString;

    static MockDbConnectionFactory()
    {
        SqlMapper.AddTypeHandler(new SqliteGuidTypeHandler());
        SqlMapper.AddTypeHandler(new SqliteDateTimeOffsetTypeHandler());
    }

    /// <summary>
    /// Initializes a new instance of MockDbConnectionFactory with an isolated in-memory SQLite database.
    /// </summary>
    public MockDbConnectionFactory()
    {
        // Unique database name per instance prevents concurrent test runner conflicts
        _connectionString = $"Data Source=InMemoryDb_{Guid.NewGuid():N};Mode=Memory;Cache=Shared";
        
        // Keep master connection open to persist shared cache in-memory SQLite database
        _masterConnection = new SqliteConnection(_connectionString);
        _masterConnection.Open();

        // Initialize schema
        InitializeSchema();
    }

    private void InitializeSchema()
    {
        using var cmd = _masterConnection.CreateCommand();
        cmd.CommandText = @"
            CREATE TABLE IF NOT EXISTS plans (
                id TEXT PRIMARY KEY COLLATE NOCASE,
                key TEXT UNIQUE COLLATE NOCASE,
                max_seats INTEGER,
                max_api_calls_per_min INTEGER,
                features TEXT,
                price_monthly_cents INTEGER
            );

            CREATE TABLE IF NOT EXISTS tenants (
                id TEXT PRIMARY KEY COLLATE NOCASE,
                name TEXT,
                tier TEXT,
                plan_id TEXT COLLATE NOCASE,
                status TEXT,
                connection_secret_ref TEXT,
                created_at TEXT,
                updated_at TEXT
            );

            CREATE TABLE IF NOT EXISTS users (
                id TEXT PRIMARY KEY COLLATE NOCASE,
                tenant_id TEXT COLLATE NOCASE,
                email TEXT COLLATE NOCASE,
                name TEXT,
                role TEXT,
                password_hash TEXT,
                mfa_secret TEXT,
                mfa_enabled INTEGER DEFAULT 0,
                status TEXT,
                created_at TEXT
            );

            CREATE TABLE IF NOT EXISTS outbox_events (
                id TEXT PRIMARY KEY COLLATE NOCASE,
                tenant_id TEXT NOT NULL COLLATE NOCASE,
                aggregate_id TEXT COLLATE NOCASE,
                event_type TEXT NOT NULL,
                payload TEXT NOT NULL,
                status TEXT DEFAULT 'pending',
                attempts INTEGER DEFAULT 0,
                last_error TEXT,
                created_at TEXT NOT NULL,
                processed_at TEXT
            );

            CREATE TABLE IF NOT EXISTS feature_flags (
                id TEXT PRIMARY KEY COLLATE NOCASE,
                tenant_id TEXT NOT NULL COLLATE NOCASE,
                flag_key TEXT NOT NULL,
                enabled INTEGER DEFAULT 0,
                rollout_percentage INTEGER DEFAULT 100,
                created_at TEXT NOT NULL
            );
            CREATE UNIQUE INDEX IF NOT EXISTS idx_feature_flags_tenant_key ON feature_flags(tenant_id, flag_key);

            CREATE TABLE IF NOT EXISTS audit_events (
                id TEXT PRIMARY KEY COLLATE NOCASE,
                tenant_id TEXT NOT NULL COLLATE NOCASE,
                actor_id TEXT COLLATE NOCASE,
                actor_email TEXT,
                action TEXT NOT NULL,
                resource_type TEXT,
                resource_id TEXT COLLATE NOCASE,
                payload_hash TEXT,
                before_state TEXT,
                after_state TEXT,
                ip_address TEXT,
                user_agent TEXT,
                ts TEXT NOT NULL
            );

            INSERT OR REPLACE INTO plans (id, key, max_seats, max_api_calls_per_min, features, price_monthly_cents)
            VALUES 
            ('00000000-0000-0000-0000-000000000001', 'starter', 5, 60, '[]', 0),
            ('00000000-0000-0000-0000-000000000002', 'growth', 25, 300, '[""advanced-analytics"", ""custom-branding""]', 4900),
            ('00000000-0000-0000-0000-000000000003', 'enterprise', 1000, 10000, '[""advanced-analytics"", ""custom-branding"", ""sso"", ""dedicated-db"", ""priority-support""]', 29900);
        ";
        cmd.ExecuteNonQuery();
    }

    /// <inheritdoc />
    public async Task<IDbConnection> CreateAsync(CancellationToken cancellationToken = default)
    {
        var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        return connection;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _masterConnection.Dispose();
    }

    // Inner Dapper type handlers for SQLite compatibility
    private sealed class SqliteGuidTypeHandler : SqlMapper.TypeHandler<Guid>
    {
        public override void SetValue(IDbDataParameter parameter, Guid value)
        {
            parameter.Value = value.ToString();
        }

        public override Guid Parse(object value)
        {
            if (value is string s && Guid.TryParse(s, out var guid))
            {
                return guid;
            }
            return Guid.Parse(value.ToString()!);
        }
    }

    private sealed class SqliteDateTimeOffsetTypeHandler : SqlMapper.TypeHandler<DateTimeOffset>
    {
        public override void SetValue(IDbDataParameter parameter, DateTimeOffset value)
        {
            parameter.Value = value.ToString("O");
        }

        public override DateTimeOffset Parse(object value)
        {
            if (value is string s && DateTimeOffset.TryParse(s, out var dto))
            {
                return dto;
            }
            return DateTimeOffset.Parse(value.ToString()!);
        }
    }
}
