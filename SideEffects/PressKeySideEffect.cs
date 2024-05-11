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
        if (!state.InternalState.CanPressKey || state.IsChatOpen)
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