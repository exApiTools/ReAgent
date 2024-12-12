using System.Linq.Dynamic.Core.CustomTypeProviders;
using System.Windows.Forms;
using ReAgent.State;

namespace ReAgent.SideEffects;

[DynamicLinqType]
[Api]
public record PressKeySideEffect(Keys Key) : ISideEffect
{
    public SideEffectApplicationResult Apply(RuleState state)
    {
        if (!state.InternalState.CanPressKey)
        {
            return SideEffectApplicationResult.UnableToApply;
        }

        if (state.InternalState.KeyToPress == Key)
        {
            return SideEffectApplicationResult.AppliedDuplicate;
        }

        if (state.InternalState.KeyToPress == null)
        {
            state.InternalState.KeyToPress = Key;
            return SideEffectApplicationResult.AppliedUnique;
        }

        return SideEffectApplicationResult.UnableToApply;
    }

    public override string ToString() => $"Press key {Key}";
}

[DynamicLinqType]
[Api]
public record StartKeyHoldSideEffect(Keys Key) : ISideEffect
{
    public SideEffectApplicationResult Apply(RuleState state)
    {
        if (state.InternalState.KeysToHoldDown.Contains(Key))
        {
            return SideEffectApplicationResult.AppliedDuplicate;
        }

        state.InternalState.KeysToHoldDown.Add(Key);
        return SideEffectApplicationResult.AppliedUnique;
    }

    public override string ToString() => $"Start holding key {Key}";
}

[DynamicLinqType]
[Api]
public record ReleaseKeyHoldSideEffect(Keys Key) : ISideEffect
{
    public SideEffectApplicationResult Apply(RuleState state)
    {
        if (state.InternalState.KeysToRelease.Contains(Key))
        {
            return SideEffectApplicationResult.AppliedDuplicate;
        }

        state.InternalState.KeysToRelease.Add(Key);
        return SideEffectApplicationResult.AppliedUnique;
    }

    public override string ToString() => $"Release key {Key}";
}