namespace Pch.Providers.ModelRoles;

public interface IModelRoleStatusSource
{
    Task<ModelRoleStatusResult> GetStatusAsync(
        ModelRoleStatusPacket packet,
        ModelRoleStatusOptions? options = null,
        CancellationToken cancellationToken = default);
}
