using System.Reflection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Farm360.Core.Configuration;

public static class OptionsRegistration
{
    /// <summary>
    /// Binds an <see cref="OptionsSectionAttribute"/> record and validates it when the host starts,
    /// so a host refuses to start on missing or invalid configuration.
    /// </summary>
    public static OptionsBuilder<T> AddPlatformOptions<T>(this IServiceCollection services, IConfiguration configuration)
        where T : class
    {
        var section = SectionName<T>();
        return services.AddOptions<T>()
            .Bind(configuration.GetSection(section))
            .ValidateDataAnnotations()
            .ValidateOnStart();
    }

    /// <summary>Binds and validates a section immediately, for code that runs before the container is built.</summary>
    public static T GetValidatedOptions<T>(this IConfiguration configuration)
        where T : class, new()
    {
        var value = configuration.GetSection(SectionName<T>()).Get<T>() ?? new T();
        var results = new List<System.ComponentModel.DataAnnotations.ValidationResult>();
        var context = new System.ComponentModel.DataAnnotations.ValidationContext(value);
        if (!System.ComponentModel.DataAnnotations.Validator.TryValidateObject(value, context, results, validateAllProperties: true))
            throw new OptionsValidationException(SectionName<T>(), typeof(T), results.Select(r => r.ErrorMessage ?? "invalid"));
        return value;
    }

    public static string SectionName<T>() =>
        typeof(T).GetCustomAttribute<OptionsSectionAttribute>()?.Name
        ?? throw new InvalidOperationException($"{typeof(T).Name} is missing [OptionsSection].");
}
