using ReAgent.State;

namespace ReAgent.SideEffects;

public enum SideEffectApplicationResult
{
    UnableToApply,
    AppliedUnique,
    AppliedDuplicate
}

public interface ISideEffect
{
    SideEffectApplicationResult Apply(RuleState state);
}