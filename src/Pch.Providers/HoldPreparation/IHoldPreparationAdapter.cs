namespace Pch.Providers.HoldPreparation;

public interface IHoldPreparationAdapter
{
    Task<HoldPreparationResult> PrepareAsync(
        HoldPreparationPacket packet,
        HoldPreparationOptions? options = null,
        CancellationToken cancellationToken = default);
}
