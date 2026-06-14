using System.Globalization;

namespace Ferret.Core.Engine.Cursor;

public static class CursorPrimaryKey
{
    public static string Encode<TKey>(TKey value) where TKey : notnull
    {
        return value switch
        {
            Guid g => g.ToString("N"),
            long l => l.ToString(CultureInfo.InvariantCulture),
            int i => i.ToString(CultureInfo.InvariantCulture),
            string s => s,
            _ => throw new NotSupportedException(
                $"Primary key type '{typeof(TKey).Name}' is not supported by Ferret cursor encoding. " +
                "Supported types: Guid, long, int, string."),
        };
    }

    public static TKey Decode<TKey>(string encoded) where TKey : notnull
    {
        if (typeof(TKey) == typeof(Guid))
            return (TKey)(object)Guid.ParseExact(encoded, "N");
        if (typeof(TKey) == typeof(long))
            return (TKey)(object)long.Parse(encoded, CultureInfo.InvariantCulture);
        if (typeof(TKey) == typeof(int))
            return (TKey)(object)int.Parse(encoded, CultureInfo.InvariantCulture);
        if (typeof(TKey) == typeof(string))
            return (TKey)(object)encoded;
        throw new NotSupportedException(
            $"Primary key type '{typeof(TKey).Name}' is not supported by Ferret cursor encoding. " +
            "Supported types: Guid, long, int, string.");
    }

    public static IReadOnlyList<string> Encode((object value, Type type)[] parts)
    {
        var result = new string[parts.Length];
        for (var i = 0; i < parts.Length; i++)
        {
            result[i] = EncodePart(parts[i].value, parts[i].type);
        }
        return result;
    }

    public static IReadOnlyList<object> Decode(IReadOnlyList<string> encoded, IReadOnlyList<Type> types)
    {
        if (encoded.Count != types.Count)
            throw new ArgumentException(
                $"Cursor primary key arity mismatch: {encoded.Count} encoded value(s) but {types.Count} type(s).");
        var result = new object[encoded.Count];
        for (var i = 0; i < encoded.Count; i++)
        {
            result[i] = DecodePart(encoded[i], types[i]);
        }
        return result;
    }

    private static string EncodePart(object value, Type type)
    {
        if (type == typeof(Guid))
            return ((Guid)value).ToString("N");
        if (type == typeof(long))
            return ((long)value).ToString(CultureInfo.InvariantCulture);
        if (type == typeof(int))
            return ((int)value).ToString(CultureInfo.InvariantCulture);
        if (type == typeof(string))
            return (string)value;
        throw new NotSupportedException(
            $"Primary key type '{type.Name}' is not supported by Ferret cursor encoding. " +
            "Supported types: Guid, long, int, string.");
    }

    private static object DecodePart(string encoded, Type type)
    {
        if (type == typeof(Guid))
            return Guid.ParseExact(encoded, "N");
        if (type == typeof(long))
            return long.Parse(encoded, CultureInfo.InvariantCulture);
        if (type == typeof(int))
            return int.Parse(encoded, CultureInfo.InvariantCulture);
        if (type == typeof(string))
            return encoded;
        throw new NotSupportedException(
            $"Primary key type '{type.Name}' is not supported by Ferret cursor encoding. " +
            "Supported types: Guid, long, int, string.");
    }
}
