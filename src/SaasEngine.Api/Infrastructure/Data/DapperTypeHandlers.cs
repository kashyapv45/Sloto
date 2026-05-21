using System;
using System.Data;
using Dapper;

namespace SaasEngine.Api.Infrastructure.Data;

/// <summary>
/// Registers explicit Dapper type handlers at application startup.
/// Required for Native AOT compatibility — prevents Dapper from relying on
/// reflection-based type discovery for custom type mappings.
/// </summary>
public static class DapperTypeHandlers
{
    private static bool _registered;

    /// <summary>
    /// Registers all custom Dapper type handlers.
    /// Safe to call multiple times — registration is idempotent.
    /// </summary>
    public static void Register()
    {
        if (_registered) return;

        SqlMapper.AddTypeHandler(new GuidTypeHandler());
        SqlMapper.AddTypeHandler(new DateTimeOffsetTypeHandler());
        SqlMapper.AddTypeHandler(new NullableGuidTypeHandler());
        SqlMapper.AddTypeHandler(new NullableDateTimeOffsetTypeHandler());

        _registered = true;
    }

    /// <summary>Explicit type handler for <see cref="Guid"/> columns.</summary>
    private sealed class GuidTypeHandler : SqlMapper.TypeHandler<Guid>
    {
        /// <inheritdoc />
        public override void SetValue(IDbDataParameter parameter, Guid value)
        {
            parameter.Value = value.ToString();
        }

        /// <inheritdoc />
        public override Guid Parse(object value)
        {
            return value switch
            {
                Guid g => g,
                string s when Guid.TryParse(s, out var guid) => guid,
                _ => Guid.Parse(value.ToString()!)
            };
        }
    }

    /// <summary>Explicit type handler for <see cref="DateTimeOffset"/> columns.</summary>
    private sealed class DateTimeOffsetTypeHandler : SqlMapper.TypeHandler<DateTimeOffset>
    {
        /// <inheritdoc />
        public override void SetValue(IDbDataParameter parameter, DateTimeOffset value)
        {
            parameter.Value = value.ToString("O");
        }

        /// <inheritdoc />
        public override DateTimeOffset Parse(object value)
        {
            return value switch
            {
                DateTimeOffset dto => dto,
                DateTime dt => new DateTimeOffset(dt, TimeSpan.Zero),
                string s when DateTimeOffset.TryParse(s, out var dto) => dto,
                _ => DateTimeOffset.Parse(value.ToString()!)
            };
        }
    }

    /// <summary>Explicit type handler for nullable <see cref="Guid"/> columns.</summary>
    private sealed class NullableGuidTypeHandler : SqlMapper.TypeHandler<Guid?>
    {
        /// <inheritdoc />
        public override void SetValue(IDbDataParameter parameter, Guid? value)
        {
            parameter.Value = value?.ToString() ?? (object)DBNull.Value;
        }

        /// <inheritdoc />
        public override Guid? Parse(object value)
        {
            if (value is null || value is DBNull) return null;
            return value switch
            {
                Guid g => g,
                string s when Guid.TryParse(s, out var guid) => guid,
                _ => Guid.Parse(value.ToString()!)
            };
        }
    }

    /// <summary>Explicit type handler for nullable <see cref="DateTimeOffset"/> columns.</summary>
    private sealed class NullableDateTimeOffsetTypeHandler : SqlMapper.TypeHandler<DateTimeOffset?>
    {
        /// <inheritdoc />
        public override void SetValue(IDbDataParameter parameter, DateTimeOffset? value)
        {
            parameter.Value = value?.ToString("O") ?? (object)DBNull.Value;
        }

        /// <inheritdoc />
        public override DateTimeOffset? Parse(object value)
        {
            if (value is null || value is DBNull) return null;
            return value switch
            {
                DateTimeOffset dto => dto,
                DateTime dt => new DateTimeOffset(dt, TimeSpan.Zero),
                string s when DateTimeOffset.TryParse(s, out var dto) => dto,
                _ => DateTimeOffset.Parse(value.ToString()!)
            };
        }
    }
}
