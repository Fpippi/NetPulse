using NetPulse.Core.SpeedTest;

namespace NetPulse.Core.Storage;

/// <summary>The speeds of the Internet contract, entered once on the Speed page.</summary>
public interface IContractStore
{
    Task InitializeAsync(CancellationToken cancellationToken = default);

    /// <summary>The saved contract, or <see cref="ContractSpeeds.None"/> when none was saved.</summary>
    Task<ContractSpeeds> GetAsync(CancellationToken cancellationToken = default);

    Task SaveAsync(ContractSpeeds contract, CancellationToken cancellationToken = default);
}
