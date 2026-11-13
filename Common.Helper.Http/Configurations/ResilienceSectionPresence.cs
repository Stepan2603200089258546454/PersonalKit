using System;
using Microsoft.Extensions.Configuration;

namespace Common.Helper.Http;

/// <summary>
/// Применяет только те разделы устойчивости, которые присутствуют в конфигурации.
/// Отсутствующий раздел <c>Resilience</c> отключает устойчивость целиком, отсутствующий
/// подраздел (<c>Retry</c>, <c>CircuitBreaker</c>, <c>AttemptTimeout</c>) отключает
/// соответствующую стратегию. Явно заданный <c>Enabled</c> имеет приоритет.
/// </summary>
internal static class ResilienceSectionPresence
{
    private const string EnabledKey = "Enabled";

    public static void Apply(ResilientHttpClientOptions options, IConfigurationSection resilienceSection)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(resilienceSection);

        var resilience = options.Resilience;
        if (resilience is null)
        {
            return;
        }

        if (!resilienceSection.Exists())
        {
            resilience.Enabled = false;
            return;
        }

        ApplyEnabled(resilienceSection, enabled => resilience.Enabled = enabled);

        ApplySubSection(
            resilienceSection.GetSection(nameof(ResilienceOptions.Retry)),
            resilience.Retry,
            static (retry, enabled) => retry.Enabled = enabled);

        ApplySubSection(
            resilienceSection.GetSection(nameof(ResilienceOptions.CircuitBreaker)),
            resilience.CircuitBreaker,
            static (circuitBreaker, enabled) => circuitBreaker.Enabled = enabled);

        ApplySubSection(
            resilienceSection.GetSection(nameof(ResilienceOptions.AttemptTimeout)),
            resilience.AttemptTimeout,
            static (timeout, enabled) => timeout.Enabled = enabled);
    }

    private static void ApplySubSection<TOptions>(
        IConfigurationSection section,
        TOptions? options,
        Action<TOptions, bool> setEnabled)
        where TOptions : class
    {
        if (options is null)
        {
            return;
        }

        ApplyEnabled(section, enabled => setEnabled(options, enabled));
    }

    /// <summary>
    /// Раздел есть и <c>Enabled</c> не задан — стратегия включается по факту наличия раздела.
    /// Раздела нет — стратегия выключается. Явное значение <c>Enabled</c> сохраняется.
    /// </summary>
    private static void ApplyEnabled(IConfigurationSection section, Action<bool> setEnabled)
    {
        if (!section.Exists())
        {
            setEnabled(false);
            return;
        }

        var enabledSection = section.GetSection(EnabledKey);
        if (!enabledSection.Exists())
        {
            setEnabled(true);
            return;
        }

        if (bool.TryParse(enabledSection.Value, out var enabled))
        {
            setEnabled(enabled);
        }
    }
}
