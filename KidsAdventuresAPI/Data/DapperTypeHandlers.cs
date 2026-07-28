namespace AdventurePacks.Api.Data;

/// <summary>
/// Dapper has no built-in mapping for <see cref="DateOnly"/>, so a birth date bound
/// straight into a command fails at run time with "cannot be used as a parameter value".
/// Registering these once at start-up lets the whole codebase use the type that
/// actually models a birth date instead of a <see cref="DateTime"/> with a fake midnight.
/// </summary>
public static class DapperTypeHandlers
{
    private static bool _registered;

    public static void Register()
    {
        if (_registered)
        {
            return;
        }

        SqlMapper.AddTypeHandler(new DateOnlyTypeHandler());
        SqlMapper.AddTypeHandler(new NullableDateOnlyTypeHandler());
        _registered = true;
    }

    private sealed class DateOnlyTypeHandler : SqlMapper.TypeHandler<DateOnly>
    {
        public override void SetValue(IDbDataParameter parameter, DateOnly value)
        {
            parameter.DbType = DbType.Date;
            parameter.Value = value.ToDateTime(TimeOnly.MinValue);
        }

        public override DateOnly Parse(object value) => value switch
        {
            DateTime dateTime => DateOnly.FromDateTime(dateTime),
            DateOnly dateOnly => dateOnly,
            string text => DateOnly.Parse(text),
            _ => throw new InvalidCastException($"Cannot convert {value.GetType()} to DateOnly.")
        };
    }

    private sealed class NullableDateOnlyTypeHandler : SqlMapper.TypeHandler<DateOnly?>
    {
        public override void SetValue(IDbDataParameter parameter, DateOnly? value)
        {
            parameter.DbType = DbType.Date;
            parameter.Value = value?.ToDateTime(TimeOnly.MinValue) ?? (object)DBNull.Value;
        }

        public override DateOnly? Parse(object? value) => value switch
        {
            null or DBNull => null,
            DateTime dateTime => DateOnly.FromDateTime(dateTime),
            DateOnly dateOnly => dateOnly,
            string text => DateOnly.Parse(text),
            _ => throw new InvalidCastException($"Cannot convert {value.GetType()} to DateOnly?.")
        };
    }
}
