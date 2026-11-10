using System.Reflection;
using System.Runtime.Loader;
using System.Text.Json;

try
{
    if (args.Length != 2) throw new ArgumentException();
    var before = Surface(args[0]);
    var after = Surface(args[1]);
    Console.WriteLine(JsonSerializer.Serialize(new { compatible = before.IsSubsetOf(after) }));
}
catch (Exception)
{
    Console.WriteLine(JsonSerializer.Serialize(new { compatible = false, error = "SDK_SURFACE_UNAVAILABLE", correlationId = Guid.NewGuid().ToString() }));
    Environment.ExitCode = 1;
}

static HashSet<string> Surface(string assemblyPath)
{
    assemblyPath = Path.GetFullPath(assemblyPath);
    var context = new LocalContext(assemblyPath);
    var assembly = context.LoadFromAssemblyPath(assemblyPath);
    var result = new HashSet<string>(StringComparer.Ordinal);
    var nullable = new NullabilityInfoContext();
    const BindingFlags flags = BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;
    foreach (var type in assembly.GetExportedTypes())
    {
        var name = type.FullName ?? throw new InvalidOperationException();
        result.Add($"type:{name}:{type.Attributes}:{type.BaseType}:{string.Join(',', type.GetInterfaces().Select(t => t.ToString()).Order())}:{Generic(type.GetGenericArguments())}");
        foreach (var field in type.GetFields(flags))
            result.Add($"field:{name}:{field.Name}:{field.FieldType}:{field.Attributes}:{Null(nullable.Create(field))}");
        foreach (var property in type.GetProperties(flags))
            result.Add($"property:{name}:{property.Name}:{property.PropertyType}:{property.GetMethod?.Attributes}:{property.SetMethod?.Attributes}:{Null(nullable.Create(property))}:{string.Join(',', property.GetIndexParameters().Select(p => Param(nullable, p)))}");
        foreach (var method in type.GetMethods(flags).Where(m => !m.IsSpecialName))
            result.Add($"method:{name}:{method.Name}:{method.Attributes}:{method.ReturnType}:{Null(nullable.Create(method.ReturnParameter))}:{Generic(method.GetGenericArguments())}:{string.Join(',', method.GetParameters().Select(p => Param(nullable, p)))}");
        foreach (var constructor in type.GetConstructors(flags))
            result.Add($"constructor:{name}:{constructor.Attributes}:{string.Join(',', constructor.GetParameters().Select(p => Param(nullable, p)))}");
        foreach (var e in type.GetEvents(flags))
            result.Add($"event:{name}:{e.Name}:{e.EventHandlerType}:{e.AddMethod?.Attributes}:{e.RemoveMethod?.Attributes}");
    }
    return result;
}
static string Param(NullabilityInfoContext ctx, ParameterInfo p) => $"{p.ParameterType}:{p.Attributes}:{p.HasDefaultValue}:{p.DefaultValue}:{Null(ctx.Create(p))}";
static string Null(NullabilityInfo value) => $"{value.ReadState}/{value.WriteState}[{string.Join(',', value.GenericTypeArguments.Select(Null))}]{(value.ElementType is null ? "" : Null(value.ElementType))}";
static string Generic(Type[] arguments) => string.Join(';', arguments.Where(t => t.IsGenericParameter).Select(t => $"{t.GenericParameterPosition}:{t.GenericParameterAttributes}:{string.Join(',', t.GetGenericParameterConstraints().Select(c => c.ToString()).Order())}"));

sealed class LocalContext(string path) : AssemblyLoadContext(isCollectible: true)
{
    private readonly AssemblyDependencyResolver resolver = new(path);
    protected override Assembly? Load(AssemblyName name)
    {
        var path = resolver.ResolveAssemblyToPath(name);
        return path is null ? null : LoadFromAssemblyPath(path);
    }
}
