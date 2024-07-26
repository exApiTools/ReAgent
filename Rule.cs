using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Dynamic.Core;
using System.Numerics;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Forms;
using ExileCore.Shared.Nodes;
using ImGuiNET;
using Newtonsoft.Json;
using ReAgent.SideEffects;
using ReAgent.State;

namespace ReAgent;

public class Rule
{
    private static readonly Vector4 RuleTrueColor = new(0, 255, 0, 255);
    private static readonly Vector4 RuleFalseColor = new(255, 255, 0, 255);
    private static readonly Vector4 RulePendingColor = new(0, 255, 128, 255);
    private static readonly Vector4 ExceptionColor = new(255, 0, 0, 255);

    private static readonly ParsingConfig ParsingConfig = new ParsingConfig()
        { AllowNewToEvaluateAnyType = true, ResolveTypesBySimpleName = true, CustomTypeProvider = new CustomDynamicLinqCustomTypeProvider() };

    public string RuleSource;
    public RuleActionType Type = RuleActionType.Key;
    public Keys? Key = Keys.D0;
    private Lazy<(Func<RuleState, IEnumerable<ISideEffect>> Func, string Exception)> _compilationResult;
    private string _lastException;
    private ulong _exceptionCounter;

    [JsonIgnore]
    public int PendingEffectCount { get; set; }

    public Rule(string ruleSource)
    {
        RuleSource = ruleSource;
        ResetFunction();
    }

    public void Display(RuleState state, bool expand)
    {
        if (expand)
        {
            if (ImguiExt.EnumerableComboBox("Action type", Enum.GetValues<RuleActionType>(), ref Type))
            {
                switch (Type)
                {
                    case RuleActionType.Key:
                        Key = Keys.D0;
                        break;
                    case RuleActionType.SingleSideEffect:
                    case RuleActionType.MultipleSideEffects:
                        Key = null;
                        break;
                }

                ResetFunction();
            }
        }
        else
        {
            ImGui.Text($"Action type: {Type}");
            ImGui.SameLine();
        }

        if (Type == RuleActionType.Key)
        {
            var key = Key.Value;
            if (expand)
            {
                var hotkeyNode = new HotkeyNode(key);
                if (hotkeyNode.DrawPickerButton($"Key {key}"))
                {
                    Key = hotkeyNode.Value;
                }
            }
            else
            {
                ImGui.Text($"Presses key {key}");
                ImGui.SameLine();
            }
        }

        if (expand)
        {
            ImGui.TextWrapped("Rule source");
            if (ImGui.InputTextMultiline(
                    "##ruleSource",
                    ref RuleSource,
                    10000,
                    new Vector2(ImGui.GetContentRegionAvail().X, ImGui.CalcTextSize($"^{RuleSource}_").Y + ImGui.GetTextLineHeight())))
            {
                ResetFunction();
            }
        }
        else
        {
            ImGui.TextUnformatted(Regex.Replace(RuleSource, "\\s+", " ") switch
            {
                { Length: > 50 } s => $"{s[..50]}...",
                var s => s
            });
            ImGui.SameLine();
        }

        var result = Evaluate(state);
        if (_lastException != null)
        {
            ImGui.PushStyleColor(ImGuiCol.Text, ExceptionColor);
            if (expand)
            {
                ImGui.TextWrapped(_lastException);
            }
            else
            {
                ImGui.TextUnformatted(_lastException);
            }

            ImGui.PopStyleColor();
        }
        else
        {
            var (color, text) = result switch
            {
                { Count: > 0 } => (RuleTrueColor, $"Rule requests: [{string.Join(", ", result)}]."),
                _ when PendingEffectCount > 0 => (RulePendingColor, $"Rule is waiting for {PendingEffectCount} side effects to apply"),
                _ => (RuleFalseColor, "Rule is idle.")
            };
            if (_exceptionCounter > 0)
            {
                text += $" There were {_exceptionCounter} before";
            }

            ImGui.TextColored(color, text);
        }
    }

    private void ResetFunction()
    {
        _exceptionCounter = 0;
        _compilationResult = new(RebuildFunction, LazyThreadSafetyMode.None);
    }

    private (Func<RuleState, IEnumerable<ISideEffect>> Func, string LastException) RebuildFunction()
    {
        try
        {
            switch (Type)
            {
                case RuleActionType.Key:
                {
                    var expression = DynamicExpressionParser.ParseLambda<RuleState, bool>(
                        ParsingConfig,
                        false,
                        RuleSource);
                    var boolFunc = expression.Compile();
                    return (s => boolFunc(s) ? new[] { new PressKeySideEffect(Key ?? throw new Exception("Key is not assigned")) } : Enumerable.Empty<ISideEffect>(), null);
                }
                case RuleActionType.SingleSideEffect:
                {
                    var expression = DynamicExpressionParser.ParseLambda<RuleState, ISideEffect>(
                        ParsingConfig,
                        false,
                        RuleSource);
                    var effectFunc = expression.Compile();
                    return (s => effectFunc(s) switch { { } sideEffect => new[] { sideEffect }, _ => Enumerable.Empty<ISideEffect>() }, null);
                }
                case RuleActionType.MultipleSideEffects:
                {
                    var expression = DynamicExpressionParser.ParseLambda<RuleState, IEnumerable<ISideEffect>>(
                        ParsingConfig,
                        false,
                        RuleSource);
                    var effectFunc = expression.Compile();
                    return (s => effectFunc(s) switch { { } sideEffects => sideEffects, _ => Enumerable.Empty<ISideEffect>() }, null);
                }
                default:
                    throw new Exception($"Invalid condition type: {Type}");
            }
        }
        catch (Exception ex)
        {
            return (null, $"Expression compilation failed: {ex.Message}");
        }
    }

    public IList<ISideEffect> Evaluate(RuleState state)
    {
        if (state == null) return [];
        IList<ISideEffect> result = null;
        var (func, compilationException) = _compilationResult.Value;
        if (func != null)
        {
            if (PendingEffectCount == 0)
            {
                try
                {
                    var intState = state.InternalState;
                    intState.AccessForbidden = true;
                    using (intState.CurrentGroupState.SetCurrentRule(this))
                    {
                        try
                        {
                            result = func(state).ToList();
                            _lastException = null;
                        }
                        finally
                        {
                            intState.AccessForbidden = false;
                        }
                    }
                }
                catch (Exception ex)
                {
                    _lastException = $"Exception while evaluating ({_exceptionCounter}): {ex.Message}";
                    _exceptionCounter++;
                }
            }
        }
        else
        {
            _lastException = compilationException;
        }

        return result ?? new List<ISideEffect>();
    }
}