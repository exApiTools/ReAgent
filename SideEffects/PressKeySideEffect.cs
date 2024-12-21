using System.Linq.Dynamic.Core.CustomTypeProviders;
using System.Windows.Forms;
using ReAgent.State;
using static ExileCore2.Shared.Nodes.HotkeyNodeV2;

namespace ReAgent.SideEffects;

[DynamicLinqType]
[Api]
public record PressKeySideEffect(HotkeyNodeValue Key) : ISideEffect
{
    public PressKeySideEffect(Keys key) : this(new HotkeyNodeValue(key))
    {
    }

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
public record StartKeyHoldSideEffect(HotkeyNodeValue Key) : ISideEffect
{
    public StartKeyHoldSideEffect(Keys key) : this(new HotkeyNodeValue(key))
    {
    }

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
public record ReleaseKeyHoldSideEffect(HotkeyNodeValue Key) : ISideEffect
{
    public ReleaseKeyHoldSideEffect(Keys key) : this(new HotkeyNodeValue(key))
    {
    }

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