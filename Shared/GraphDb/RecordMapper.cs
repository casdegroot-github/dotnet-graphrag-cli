using System.Reflection;
using Neo4j.Driver;

namespace GraphRagCli.Shared.GraphDb;

public static class RecordMapper
{
    public static async Task<List<T>> MapAsync<T>(this Task<EagerResult<IReadOnlyList<IRecord>>> task)
    {
        var (records, _, _) = await task;
        return records.Select(Map<T>).ToList();
    }

    public static T Map<T>(this IRecord record)
    {
        var type = typeof(T);

        // Primitives / single-column: just convert the first value
        if (type.IsPrimitive || type == typeof(string))
            return (T)Convert(record[0], type)!;

        var ctor = type.GetConstructors()
            .OrderByDescending(c => c.GetParameters().Length)
            .First();

        var parameters = ctor.GetParameters();
        var isTuple = type.FullName?.StartsWith("System.ValueTuple") == true;

        var args = isTuple
            ? parameters.Select((p, i) => Convert(record[i], p.ParameterType)).ToArray()
            : parameters.Select(p => GetValue(record, p)).ToArray();

        return (T)ctor.Invoke(args);
    }

    private static object? GetValue(IRecord record, ParameterInfo param)
    {
        var key = record.Keys.FirstOrDefault(k =>
            string.Equals(k, param.Name, StringComparison.OrdinalIgnoreCase));

        if (key == null)
            return param.HasDefaultValue ? param.DefaultValue : null;

        var raw = record[key];
        if (raw is null)
            return null;

        return Convert(raw, param.ParameterType);
    }

    private static object? Convert(object value, Type target)
    {
        var underlying = Nullable.GetUnderlyingType(target) ?? target;

        if (underlying.IsInstanceOfType(value))
            return value;

        if (underlying == typeof(string)) return value.ToString();
        if (underlying == typeof(int)) return System.Convert.ToInt32(value);
        if (underlying == typeof(long)) return System.Convert.ToInt64(value);
        if (underlying == typeof(double)) return System.Convert.ToDouble(value);
        if (underlying == typeof(float)) return System.Convert.ToSingle(value);
        if (underlying == typeof(bool)) return System.Convert.ToBoolean(value);

        if (underlying.IsGenericType && underlying.GetGenericTypeDefinition() == typeof(List<>))
        {
            var elementType = underlying.GetGenericArguments()[0];
            if (value is IEnumerable<object> enumerable)
            {
                var list = (System.Collections.IList)Activator.CreateInstance(underlying)!;
                foreach (var item in enumerable)
                    list.Add(Convert(item, elementType));
                return list;
            }
        }

        return value;
    }
}
