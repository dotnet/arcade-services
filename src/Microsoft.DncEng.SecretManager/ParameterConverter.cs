using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace Microsoft.DncEng.SecretManager;

public static class ParameterConverter
{
    public static TParameters ConvertParameters<TParameters>(IDictionary<string, object> parameters)
    {
        return (TParameters)ConvertValue(parameters, typeof(TParameters));
    }

    public static object ConvertValue(object value, Type type)
    {
        if (IsDictionary(value))
        {
            return ConvertObject(value, type);
        }

        return ConvertScalar(value, type);
    }

    private static bool IsDictionary(object value)
    {
        if (value == null)
        {
            return false;
        }

        var type = value.GetType();
        if (type.GetInterfaces().Any(iface => iface.Name.Contains("Dictionary")))
        {
            return true;
        }

        return false;
    }

    private static object ConvertObject(object value, Type type)
    {
        var ciDict = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
        foreach (dynamic entry in (IEnumerable) value)
        {
            ciDict.Add(entry.Key.ToString(), entry.Value);
        }
        var result = Activator.CreateInstance(type);
        foreach (PropertyInfo property in type.GetProperties())
        {
            if (ciDict.TryGetValue(property.Name, out object propertyValue))
            {
                property.SetValue(result, ConvertValue(propertyValue, property.PropertyType));
            }
        }

        return result;
    }

    private static object ConvertScalar(object value, Type type)
    {
        if (value == null)
        {
            return null;
        }
        if (type == typeof(Guid))
        {
            return Guid.Parse(value.ToString());
        }

        if (type.GetConstructor(new[] {typeof(string)}) != null)
        {
            return Activator.CreateInstance(type, value.ToString());
        }

        return Convert.ChangeType(value, type);
    }
}
