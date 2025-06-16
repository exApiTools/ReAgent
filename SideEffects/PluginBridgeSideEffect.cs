using System;
using System.Linq.Dynamic.Core.CustomTypeProviders;
using ReAgent.State;

namespace ReAgent.SideEffects;

[DynamicLinqType]
[Api]
public record PluginBridgeSideEffect<T>(string MethodName, Action<T> InvokeFunctionAction) : ISideEffect where T : Delegate
{
    public SideEffectApplicationResult Apply(RuleState state)
    {
        state.InternalState.PluginBridgeMethodsToCall.Add((MethodName, d => InvokeFunctionAction((T)d)));
        return SideEffectApplicationResult.AppliedUnique;
    }

    public override string ToString() => $"Invoke PluginBridge.{MethodName}";
}