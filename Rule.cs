using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Dynamic.Core;
using System.Numerics;
using System.Reflection;
using System.Reflection.Metadata;
using System.Runtime.Loader;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Forms;
using ExileCore2;
using ExileCore2.Shared.Enums;
using ExileCore2.Shared.Nodes;
using ImGuiNET;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Scripting;
using Microsoft.CodeAnalysis.Scripting.Hosting;
using Newtonsoft.Json;
using ReAgent.SideEffects;
using ReAgent.State;
using static ExileCore2.Shared.Nodes.HotkeyNodeV2;

namespace ReAgent;

public class Rule
{
    private static readonly Vector4 RuleTrueColor = new(0, 255, 0, 255);
    private static readonly Vector4 RuleFalseColor = new(255, 255, 0, 255);
    private static readonly Vector4 RulePendingColor = new(0, 255, 128, 255);
    private static readonly Vector4 ExceptionColor = new(255, 0, 0, 255);

    private static readonly ParsingConfig ParsingConfig = new ParsingConfig()
        { AllowNewToEvaluateAnyType = true, ResolveTypesBySimpleName = true, CustomTypeProvider = new CustomDynamicLinqCustomTypeProvider() };

    static Rule()
    {
        unsafe
        {
            Assembly.GetExecutingAssembly().TryGetRawMetadata(out byte* blob, out int length);
            var moduleMetadata = ModuleMetadata.CreateFromMetadata((IntPtr)blob, length);
            var assemblyMetadata = AssemblyMetadata.Create(moduleMetadata);
            metadataReference = assemblyMetadata.GetReference();
            loader = new InteractiveAssemblyLoader();
            loader.RegisterDependency(typeof(ReAgent).Assembly);
        }
    }

    private ScriptOptions ScriptOptions => ScriptOptions.Default
        .AddReferences(
            typeof(Vector2).Assembly,
            typeof(GameStat).Assembly,
            typeof(Core).Assembly)
        .AddReferences(typeof(Keys).Assembly)
        .AddReferences(metadataReference)
        .AddImports(
            "System.Collections.Generic", "System.Linq", "System.Numerics", "System.Windows.Forms",
            "ReAgent", "ReAgent.State", "ReAgent.SideEffects",
            "ExileCore2", "ExileCore2.Shared", "ExileCore2.Shared.Enums",
            "ExileCore2.Shared.Helpers", "ExileCore2.PoEMemory.Components", "ExileCore2.PoEMemory.MemoryObjects",
            "ExileCore2.PoEMemory", "ExileCore2.PoEMemory.FilesInMemory"
        );

    public string RuleSource;
    public RuleActionType Type = RuleActionType.Key;

    public Keys? Key
    {
        get
        {
            return KeyV2 switch { { Key: { } k } => k, _ => null };
        }
        set
        {
            KeyV2 = value switch { null => null, { } k => new HotkeyNodeValue(k) };
        }
    }

    public bool ShouldSerializeKey() => false;

    public HotkeyNodeValue KeyV2 = new HotkeyNodeValue(Keys.D0);
    public int SyntaxVersion;
    private Lazy<(Func<RuleState, IEnumerable<ISideEffect>> Func, string Exception)> _compilationResult;
    private string _lastException;
    private ulong _exceptionCounter;
    private static readonly InteractiveAssemblyLoader loader;
    private static readonly PortableExecutableReference metadataReference;

    [JsonIgnore]
    public int PendingEffectCount { get; set; }

    public Rule(string ruleSource, int? syntaxVersion)
    {
        RuleSource = ruleSource;
        SyntaxVersion = syntaxVersion ?? 1;
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
                        KeyV2 = new HotkeyNodeValue(Keys.D0);
                        break;
                    case RuleActionType.SingleSideEffect:
                    case RuleActionType.MultipleSideEffects:
                        KeyV2 = null;
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
            var key = KeyV2;
            if (expand)
            {
                var hotkeyNode = new HotkeyNodeV2(key) { AllowControllerKeys = true };
                if (hotkeyNode.DrawPickerButton($"Key {key}"))
                {
                    KeyV2 = hotkeyNode.Value;
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
            ImGui.SameLine();
            var syntaxState = SyntaxVersion switch { 1 => false, 2 => true };
            if (ImGui.Checkbox("Use new syntax", ref syntaxState))
            {
                SyntaxVersion = syntaxState ? 2 : 1;
                ResetFunction();
            }

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
        _compilationResult = new(SyntaxVersion switch { 1 => RebuildFunctionV1, 2 => RebuildFunctionV2 }, LazyThreadSafetyMode.None);
    }

    private (Func<RuleState, IEnumerable<ISideEffect>> Func, string LastException) RebuildFunctionV1()
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
                    return (s => boolFunc(s) ? [new PressKeySideEffect(KeyV2 ?? throw new Exception("Key is not assigned"))] : [], null);
                }
                case RuleActionType.SingleSideEffect:
                {
                    var expression = DynamicExpressionParser.ParseLambda<RuleState, ISideEffect>(
                        ParsingConfig,
                        false,
                        RuleSource);
                    var effectFunc = expression.Compile();
                    return (s => effectFunc(s) switch { { } sideEffect => [sideEffect], _ => Enumerable.Empty<ISideEffect>() }, null);
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

    private delegate T ScriptFunc<T>(RuleState State);


    private (Func<RuleState, IEnumerable<ISideEffect>> Func, string LastException) RebuildFunctionV2()
    {
        try
        {
            switch (Type)
            {
                case RuleActionType.Key:
                {
                    var @delegate = DelegateCompiler.CompileDelegate<ScriptFunc<bool>>(RuleSource, ScriptOptions, CreateAlc());
                    return (s => @delegate(s)
                        ? [new PressKeySideEffect(KeyV2 ?? throw new Exception("Key is not assigned"))]
                        : [], null);
                }
                case RuleActionType.SingleSideEffect:
                {
                    var @delegate = DelegateCompiler.CompileDelegate<ScriptFunc<ISideEffect>>(RuleSource, ScriptOptions, CreateAlc());
                    return (s => @delegate(s) switch { { } sideEffect => [sideEffect], _ => Enumerable.Empty<ISideEffect>() },
                        null);
                }
                case RuleActionType.MultipleSideEffects:
                {
                    var @delegate = DelegateCompiler.CompileDelegate<ScriptFunc<IEnumerable<ISideEffect>>>(RuleSource, ScriptOptions, CreateAlc());
                    return (s => @delegate(s) switch { { } sideEffects => sideEffects, _ => Enumerable.Empty<ISideEffect>() }, null);
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

    private static AssemblyLoadContext CreateAlc()
    {
        var assemblyLoadContext = new AssemblyLoadContext($"bbb{Guid.NewGuid()}", true);
        assemblyLoadContext.Resolving += (context, name) => name.Name == "ReAgent" ? Assembly.GetExecutingAssembly() : null;
        return assemblyLoadContext;
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
