using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Dynamic.Core;
using System.Numerics;
using System.Text.RegularExpressions;
using System.Windows.Forms;
using ExileCore.Shared.Nodes;
using ImGuiNET;
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
    private string _lastException;
    private Func<RuleState, IEnumerable<ISideEffect>> _func;
    private ulong _exceptionCounter;

    public int PendingEffectCount { get; set; }

    public Rule(string ruleSource)
    {
        RuleSource = ruleSource;
        RebuildFunction();
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

                RebuildFunction();
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
                RebuildFunction();
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
            var result = Evaluate(state);
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

    private void RebuildFunction()
    {
        try
        {
            _exceptionCounter = 0;
            switch (Type)
            {
                case RuleActionType.Key:
                {
                    var expression = DynamicExpressionParser.ParseLambda<RuleState, bool>(
                        ParsingConfig,
                        false,
                        RuleSource);
                    var boolFunc = expression.Compile();
                    _func = s => boolFunc(s) ? new[] { new PressKeySideEffect(Key ?? throw new Exception("Key is not assigned")) } : Enumerable.Empty<ISideEffect>();
                    break;
                }
                case RuleActionType.SingleSideEffect:
                {
                    var expression = DynamicExpressionParser.ParseLambda<RuleState, ISideEffect>(
                        ParsingConfig,
                        false,
                        RuleSource);
                    var effectFunc = expression.Compile();
                    _func = s => effectFunc(s) switch { { } sideEffect => new[] { sideEffect }, _ => Enumerable.Empty<ISideEffect>() };
                    break;
                }
                case RuleActionType.MultipleSideEffects:
                {
                    var expression = DynamicExpressionParser.ParseLambda<RuleState, IEnumerable<ISideEffect>>(
                        ParsingConfig,
                        false,
                        RuleSource);
                    var effectFunc = expression.Compile();
                    _func = s => effectFunc(s) switch { { } sideEffects => sideEffects, _ => Enumerable.Empty<ISideEffect>() };
                    break;
                }
                default:
                    throw new Exception($"Invalid condition type: {Type}");
            }

            _lastException = null;
        }
        catch (Exception ex)
        {
            _lastException = $"Expression compilation failed: {ex.Message}";
            _func = null;
        }
    }

    public IList<ISideEffect> Evaluate(RuleState state)
    {
        IList<ISideEffect> result = null;
        if (_func != null && PendingEffectCount == 0)
        {
            try
            {
                var intState = state.InternalState;
                intState.AccessForbidden = true;
                using (intState.CurrentGroupState.SetCurrentRule(this))
                {
                    try
                    {
                        result = _func(state).ToList();
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

        return result ?? new List<ISideEffect>();
    }
}