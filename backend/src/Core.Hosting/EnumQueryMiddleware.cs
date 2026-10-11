using System.Collections.Concurrent;
using System.Reflection;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Primitives;

namespace Acme.Core.Hosting;

/// <summary>
/// Matches enum values in the query string case-insensitively. ASP.NET binds an enum parameter case-sensitively (<c>Draft</c>
/// binds, <c>draft</c> is a 400), while this platform writes and documents enums in camelCase (<c>draft</c>), and the generated
/// clients send what the document says. Before binding, a value that matches a member by name, ignoring case, is rewritten to the
/// member's own name. Numbers and unknown values are left alone, so they still fail binding as before.
/// </summary>
/// <remarks>
/// Covers enum-typed parameters of an endpoint handler (including nullable ones). Enums inside an <c>[AsParameters]</c> record are
/// not rewritten; declare such a filter as a parameter of the handler.
/// </remarks>
internal sealed class EnumQueryMiddleware(RequestDelegate next)
{
    private static readonly ConcurrentDictionary<MethodInfo, EnumParameter[]> Cache = new();

    public Task InvokeAsync(HttpContext context)
    {
        if (context.Request.QueryString.HasValue && context.GetEndpoint()?.Metadata.GetMetadata<MethodInfo>() is { } handler)
        {
            var parameters = Cache.GetOrAdd(handler, static method => [.. method.GetParameters()
                .Select(p => (Name: p.Name, Type: EnumTypeOf(p.ParameterType)))
                .Where(p => p.Name is not null && p.Type is not null)
                .Select(p => new EnumParameter(p.Name!, [.. Enum.GetNames(p.Type!)]))]);
            if (parameters.Length > 0)
                Rewrite(context.Request, parameters);
        }
        return next(context);
    }

    /// <summary>The enum a parameter binds, whether it is the enum, a nullable enum, or an array or list of them; otherwise null.</summary>
    private static Type? EnumTypeOf(Type parameter)
    {
        var type = Nullable.GetUnderlyingType(parameter) ?? parameter;
        if (type.IsArray)
            type = type.GetElementType()!;
        else if (type.IsGenericType && type.GetGenericArguments().Length == 1 && typeof(System.Collections.IEnumerable).IsAssignableFrom(type))
            type = type.GetGenericArguments()[0];
        type = Nullable.GetUnderlyingType(type) ?? type;
        return type.IsEnum ? type : null;
    }

    private static void Rewrite(HttpRequest request, EnumParameter[] parameters)
    {
        Dictionary<string, StringValues>? changed = null;
        foreach (var parameter in parameters)
        {
            if (!request.Query.TryGetValue(parameter.Name, out var values))
                continue;
            var mapped = values.Select(value => parameter.Canonical(value)).ToArray();
            if (mapped.SequenceEqual(values))
                continue;
            changed ??= request.Query.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.OrdinalIgnoreCase);
            changed[parameter.Name] = new StringValues(mapped);
        }
        if (changed is not null)
            request.QueryString = QueryString.Create(changed.SelectMany(pair => pair.Value.Select(value => KeyValuePair.Create(pair.Key, value))));
    }

    private sealed record EnumParameter(string Name, string[] Members)
    {
        public string? Canonical(string? value)
        {
            if (string.IsNullOrEmpty(value))
                return value;
            foreach (var member in Members)
            {
                if (member.Equals(value, StringComparison.OrdinalIgnoreCase))
                    return member;
            }
            return value;
        }
    }
}
