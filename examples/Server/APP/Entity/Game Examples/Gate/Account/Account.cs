using Fantasy.Entitas;
using Fantasy.Network;

namespace Fantasy;

public sealed class Account : Entity
{
    public string Name = string.Empty;

    public EntityReference<Session> Session;
}
