using NetPulse.Core.Storage;

namespace NetPulse.Core.Targets;

/// <summary>Outcome of <see cref="TargetManager.AddAsync"/>.</summary>
/// <param name="Success">True if the target was added.</param>
/// <param name="Target">The normalized target name (empty when the input was invalid).</param>
/// <param name="Error">Why the target was rejected, null on success.</param>
public sealed record AddTargetResult(bool Success, string Target, string? Error);

/// <summary>A monitored target and whether the user may remove it.</summary>
/// <param name="Name">Target name as stored in samples.</param>
/// <param name="IsCustom">True for targets added from the dashboard, false for configured ones.</param>
public sealed record MonitoredTarget(string Name, bool IsCustom);

/// <summary>
/// Single entry point for the list of monitored targets: configured targets (read-only)
/// plus custom targets that users add and remove at runtime.
/// </summary>
public sealed class TargetManager(
    IReadOnlyList<string> configuredTargets,
    ITargetStore targetStore,
    ISampleStore sampleStore,
    int maxCustomTargets)
{
    /// <summary>Raised after a custom target has been added or removed.</summary>
    public event Action? Changed;

    public int MaxCustomTargets => maxCustomTargets;

    /// <summary>All targets to ping: configured first, then custom, without duplicates.</summary>
    public async Task<IReadOnlyList<string>> GetEffectiveTargetsAsync(CancellationToken cancellationToken = default) =>
        TargetList.Merge(configuredTargets, await targetStore.GetAllAsync(cancellationToken));

    /// <summary>All targets with a flag telling which ones can be removed.</summary>
    public async Task<IReadOnlyList<MonitoredTarget>> GetTargetsAsync(CancellationToken cancellationToken = default)
    {
        var custom = await targetStore.GetAllAsync(cancellationToken);
        var configured = new HashSet<string>(configuredTargets, StringComparer.OrdinalIgnoreCase);

        return TargetList.Merge(configuredTargets, custom)
            .Select(t => new MonitoredTarget(t, !configured.Contains(t)))
            .ToList();
    }

    public async Task<AddTargetResult> AddAsync(string? input, CancellationToken cancellationToken = default)
    {
        if (!TargetName.TryNormalize(input, out var target, out var error))
        {
            return new AddTargetResult(false, string.Empty, error);
        }

        if (configuredTargets.Contains(target, StringComparer.OrdinalIgnoreCase))
        {
            return new AddTargetResult(false, target, $"{target} is already monitored.");
        }

        var custom = await targetStore.GetAllAsync(cancellationToken);
        if (custom.Contains(target, StringComparer.OrdinalIgnoreCase))
        {
            return new AddTargetResult(false, target, $"{target} is already monitored.");
        }

        if (custom.Count >= maxCustomTargets)
        {
            return new AddTargetResult(false, target, $"You can add at most {maxCustomTargets} custom targets.");
        }

        if (!await targetStore.AddAsync(target, cancellationToken))
        {
            return new AddTargetResult(false, target, $"{target} is already monitored.");
        }

        Changed?.Invoke();
        return new AddTargetResult(true, target, null);
    }

    /// <summary>
    /// Stops monitoring a custom target and deletes its history.
    /// Configured targets cannot be removed and return false.
    /// </summary>
    public async Task<bool> RemoveAsync(string target, CancellationToken cancellationToken = default)
    {
        if (configuredTargets.Contains(target, StringComparer.OrdinalIgnoreCase)
            || !await targetStore.RemoveAsync(target, cancellationToken))
        {
            return false;
        }

        await sampleStore.DeleteTargetAsync(target, cancellationToken);
        Changed?.Invoke();
        return true;
    }
}
