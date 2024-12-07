using ExileCore2.PoEMemory.Components;

namespace ReAgent.State;

[Api]
public class VitalsInfo
{
    [Api]
    public Vital HP { get; }

    [Api]
    public Vital ES { get; }

    [Api]
    public Vital Mana { get; }

    public VitalsInfo(Life lifeComponent)
    {
        HP = Vital.From(lifeComponent.Health);
        ES = Vital.From(lifeComponent.EnergyShield);
        Mana = Vital.From(lifeComponent.Mana);
    }
}