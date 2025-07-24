using System.Reflection;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace DysonNetwork.Shared.Data;

public static class JsonExtensions
{
    public static Action<JsonTypeInfo> UnignoreAllProperties() =>
        typeInfo =>
        {
            if (typeInfo.Kind == JsonTypeInfoKind.Object)
                // [JsonIgnore] is implemented by setting ShouldSerialize to a function that returns false.
                foreach (var property in typeInfo.Properties.Where(ShouldUnignore))
                {
                    property.Get ??= CreatePropertyGetter(property);
                    property.Set ??= CreatePropertySetter(property);
                    if (property.Get != null)
                        property.ShouldSerialize = null;
                }
        };
    
    public static Action<JsonTypeInfo> UnignoreAllProperties(Type type) =>
        typeInfo =>
        {
            if (type.IsAssignableFrom(typeInfo.Type) && typeInfo.Kind == JsonTypeInfoKind.Object)
                // [JsonIgnore] is implemented by setting ShouldSerialize to a function that returns false.
                foreach (var property in typeInfo.Properties.Where(ShouldUnignore))
                {
                    property.Get ??= CreatePropertyGetter(property);
                    property.Set ??= CreatePropertySetter(property);
                    if (property.Get != null)
                        property.ShouldSerialize = null;
                }
        };
    
    public static Action<JsonTypeInfo> UnignoreProperties(Type type, params string[] properties) =>
        typeInfo =>
        {
            if (type.IsAssignableFrom(typeInfo.Type) && typeInfo.Kind == JsonTypeInfoKind.Object)
                // [JsonIgnore] is implemented by setting ShouldSerialize to a function that returns false.
                foreach (var property in typeInfo.Properties.Where(p => ShouldUnignore(p, properties)))
                {
                    property.Get ??= CreatePropertyGetter(property);
                    property.Set ??= CreatePropertySetter(property);
                    if (property.Get != null)
                        property.ShouldSerialize = null;
                }
        };

    public static Action<JsonTypeInfo> UnignorePropertiesForDeserialize(Type type, params string[] properties) =>
        typeInfo =>
        {
            if (type.IsAssignableFrom(typeInfo.Type) && typeInfo.Kind == JsonTypeInfoKind.Object)
                // [JsonIgnore] is implemented by setting ShouldSerialize to a function that returns false.
                foreach (var property in typeInfo.Properties.Where(p => ShouldUnignore(p, properties)))
                {
                    property.Set ??= CreatePropertySetter(property);
                }
        };

    static bool ShouldUnignore(JsonPropertyInfo property) =>
        property.ShouldSerialize != null &&
        property.AttributeProvider?.IsDefined(typeof(JsonIgnoreAttribute), true) == true;

    static bool ShouldUnignore(JsonPropertyInfo property, string[] properties) =>
        property.ShouldSerialize != null &&
        property.AttributeProvider?.IsDefined(typeof(JsonIgnoreAttribute), true) == true &&
        properties.Contains(property.GetMemberName());

    // CreateGetter() and CreateSetter() taken from this answer https://stackoverflow.com/a/76296944/3744182
    // To https://stackoverflow.com/questions/61869393/get-net-core-jsonserializer-to-serialize-private-members

    delegate TValue RefFunc<TObject, TValue>(ref TObject arg);

    static Func<object, object?>? CreatePropertyGetter(JsonPropertyInfo property) =>
        property.GetPropertyInfo() is { } info && info.ReflectedType != null && info.GetGetMethod() is { } getMethod
            ? CreateGetter(info.ReflectedType, getMethod)
            : null;

    static Func<object, object?>? CreateGetter(Type type, MethodInfo? method)
    {
        if (method == null)
            return null;
        var myMethod = typeof(JsonExtensions).GetMethod(nameof(JsonExtensions.CreateGetterGeneric),
            BindingFlags.NonPublic | BindingFlags.Static)!;
        return (Func<object, object?>)(myMethod.MakeGenericMethod(new[] { type, method.ReturnType })
            .Invoke(null, new[] { method })!);
    }

    static Func<object, object?> CreateGetterGeneric<TObject, TValue>(MethodInfo method)
    {
        if (method == null)
            throw new ArgumentNullException();
        if (typeof(TObject).IsValueType)
        {
            // https://stackoverflow.com/questions/4326736/how-can-i-create-an-open-delegate-from-a-structs-instance-method
            // https://stackoverflow.com/questions/1212346/uncurrying-an-instance-method-in-net/1212396#1212396
            var func = (RefFunc<TObject, TValue>)Delegate.CreateDelegate(typeof(RefFunc<TObject, TValue>), null,
                method);
            return (o) =>
            {
                var tObj = (TObject)o;
                return func(ref tObj);
            };
        }
        else
        {
            var func = (Func<TObject, TValue>)Delegate.CreateDelegate(typeof(Func<TObject, TValue>), method);
            return (o) => func((TObject)o);
        }
    }

    static Action<object, object?>? CreatePropertySetter(JsonPropertyInfo property) =>
        property.GetPropertyInfo() is { } info && info.ReflectedType != null && info.GetSetMethod() is { } setMethod
            ? CreateSetter(info.ReflectedType, setMethod)
            : null;

    static Action<object, object?>? CreateSetter(Type type, MethodInfo? method)
    {
        if (method == null)
            return null;
        var myMethod = typeof(JsonExtensions).GetMethod(nameof(JsonExtensions.CreateSetterGeneric),
            BindingFlags.NonPublic | BindingFlags.Static)!;
        return (Action<object, object?>)(myMethod
            .MakeGenericMethod(new[] { type, method.GetParameters().Single().ParameterType })
            .Invoke(null, new[] { method })!);
    }

    static Action<object, object?>? CreateSetterGeneric<TObject, TValue>(MethodInfo method)
    {
        if (method == null)
            throw new ArgumentNullException();
        if (typeof(TObject).IsValueType)
        {
            // TODO: find a performant way to do this.  Possibilities:
            // Box<T> from Microsoft.Toolkit.HighPerformance
            // https://stackoverflow.com/questions/18937935/how-to-mutate-a-boxed-struct-using-il
            return (o, v) => method.Invoke(o, new[] { v });
        }
        else
        {
            var func = (Action<TObject, TValue?>)Delegate.CreateDelegate(typeof(Action<TObject, TValue?>), method);
            return (o, v) => func((TObject)o, (TValue?)v);
        }
    }

    static PropertyInfo? GetPropertyInfo(this JsonPropertyInfo property) =>
        (property.AttributeProvider as PropertyInfo);

    static string? GetMemberName(this JsonPropertyInfo property) => (property.AttributeProvider as MemberInfo)?.Name;
}