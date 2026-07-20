using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Dynamic.Core.CustomTypeProviders;
using System.Numerics;
using System.Reflection;
using System.Windows.Forms;
using ExileCore.Shared.Enums;
using ReAgent.State;

namespace ReAgent.Autocomplete;

public sealed record CompletionItem(string Label, string InsertText, string Detail, int Rank);

public sealed class CompletionResult
{
    public static readonly CompletionResult Empty = new() { Items = [], ReplaceStart = 0, AutoShow = false };

    public List<CompletionItem> Items { get; init; } = [];
    public int ReplaceStart { get; init; }
    public bool AutoShow { get; init; }
}

/// <summary>
/// Produces completions for rule expressions by reflecting over the same surface the two rule
/// engines bind against: [Api]-marked members for the RuleState graph, [DynamicLinqType] types for
/// constructible side effects. Never throws — a completion failure must never break the editor.
/// </summary>
public static class CompletionEngine
{
    private const int MaxItems = 50;

    private readonly record struct Segment(string Name, bool HasCall, bool HasIndex);

    private static readonly Dictionary<Type, List<CompletionItem>> MemberCache = [];
    private static readonly Dictionary<Type, string[]> EnumNameCache = [];
    private static Dictionary<string, Type> _typeMap;

    /// <summary>Custom ailment names from the plugin's CustomAilments.json, set at plugin init.</summary>
    public static IReadOnlyList<string> CustomAilmentNames { get; set; } = [];

    // Dynamic LINQ supports these on IEnumerable<T>; value maps method name to how T flows through.
    private static readonly Dictionary<string, Func<Type, Type>> LinqMethods = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Where"] = t => typeof(IEnumerable<>).MakeGenericType(t),
        ["OrderBy"] = t => typeof(IEnumerable<>).MakeGenericType(t),
        ["OrderByDescending"] = t => typeof(IEnumerable<>).MakeGenericType(t),
        ["Take"] = t => typeof(IEnumerable<>).MakeGenericType(t),
        ["Skip"] = t => typeof(IEnumerable<>).MakeGenericType(t),
        ["Distinct"] = t => typeof(IEnumerable<>).MakeGenericType(t),
        ["ToList"] = t => typeof(IEnumerable<>).MakeGenericType(t),
        ["ToArray"] = t => typeof(IEnumerable<>).MakeGenericType(t),
        ["First"] = t => t,
        ["FirstOrDefault"] = t => t,
        ["Last"] = t => t,
        ["LastOrDefault"] = t => t,
        ["Single"] = t => t,
        ["SingleOrDefault"] = t => t,
        ["MinBy"] = t => t,
        ["MaxBy"] = t => t,
        ["Any"] = _ => typeof(bool),
        ["All"] = _ => typeof(bool),
        ["Contains"] = _ => typeof(bool),
        ["Count"] = _ => typeof(int),
        ["Sum"] = _ => typeof(double),
        ["Average"] = _ => typeof(double),
        ["Min"] = _ => typeof(double),
        ["Max"] = _ => typeof(double),
    };

    // Lambda-shaped LINQ calls where the inner expression binds to the element type directly (v1 syntax).
    private static readonly HashSet<string> LambdaMethods = new(StringComparer.OrdinalIgnoreCase)
    {
        "Where", "Any", "All", "First", "FirstOrDefault", "Last", "LastOrDefault", "Single", "SingleOrDefault",
        "Count", "OrderBy", "OrderByDescending", "Sum", "Average", "Min", "Max", "MinBy", "MaxBy", "Select",
        "TakeWhile", "SkipWhile",
    };

    public static CompletionResult GetCompletions(string text, int caret, int syntaxVersion, RuleActionType actionType, RuleState state)
    {
        try
        {
            return GetCompletionsUnsafe(text ?? "", Math.Clamp(caret, 0, text?.Length ?? 0), syntaxVersion, actionType, state);
        }
        catch
        {
            return CompletionResult.Empty;
        }
    }

    private static CompletionResult GetCompletionsUnsafe(string text, int caret, int syntaxVersion, RuleActionType actionType, RuleState state)
    {
        var scan = Scan(text, caret);

        if (scan.InString)
        {
            return StringContextCompletions(text, caret, scan.StringStart, syntaxVersion, state);
        }

        var tokenStart = caret;
        while (tokenStart > 0 && IsIdentChar(text[tokenStart - 1]))
        {
            tokenStart--;
        }

        var prefix = text[tokenStart..caret];
        // Numeric literal like `50` — never complete inside it.
        if (prefix.Length > 0 && char.IsDigit(prefix[0]))
        {
            return CompletionResult.Empty;
        }

        var afterDot = tokenStart > 0 && text[tokenStart - 1] == '.';
        var lambdaElement = syntaxVersion == 1 ? FindLambdaElement(text, scan.OpenParens, state) : null;

        List<CompletionItem> pool;
        if (afterDot)
        {
            var chain = ParseChainBackward(text, tokenStart - 1);
            if (chain == null)
            {
                return CompletionResult.Empty;
            }

            var resolved = ResolveChain(chain, syntaxVersion, lambdaElement);
            if (resolved.Type == null)
            {
                return CompletionResult.Empty;
            }

            pool = resolved.IsStatic ? StaticMembersOf(resolved.Type) : InstanceMembersOf(resolved.Type);
        }
        else
        {
            pool = RootCompletions(text, tokenStart, syntaxVersion, actionType, lambdaElement);
        }

        var items = Filter(pool, prefix);
        var autoShow = items.Count > 0 &&
                       (prefix.Length > 0 || afterDot) &&
                       !(items.Count == 1 && string.Equals(items[0].InsertText, prefix, StringComparison.Ordinal));
        return new CompletionResult { Items = items, ReplaceStart = tokenStart, AutoShow = autoShow };
    }

    private readonly record struct ScanState(bool InString, int StringStart, List<int> OpenParens);

    private static ScanState Scan(string text, int caret)
    {
        var inString = false;
        var stringStart = -1;
        var parens = new List<int>();
        var brackets = 0;
        for (var i = 0; i < caret; i++)
        {
            var c = text[i];
            if (inString)
            {
                if (c == '"' && text[i - 1] != '\\')
                {
                    inString = false;
                    stringStart = -1;
                }

                continue;
            }

            switch (c)
            {
                case '"':
                    inString = true;
                    stringStart = i;
                    break;
                case '(':
                    parens.Add(i);
                    break;
                case ')':
                    if (parens.Count > 0) parens.RemoveAt(parens.Count - 1);
                    break;
                case '[':
                    brackets++;
                    break;
                case ']':
                    if (brackets > 0) brackets--;
                    break;
            }
        }

        return new ScanState(inString, stringStart, parens);
    }

    private static bool IsIdentChar(char c) => char.IsLetterOrDigit(c) || c == '_';

    /// <summary>
    /// Parses the member chain ending at <paramref name="dotPos"/> (which must point at a '.'),
    /// walking backwards over `Name`, `Name(...)` and `Name[...]` segments.
    /// </summary>
    private static List<Segment> ParseChainBackward(string text, int dotPos)
    {
        var segs = new List<Segment>();
        var i = dotPos;
        while (true)
        {
            i--;
            bool hasCall = false, hasIndex = false;
            while (i >= 0 && text[i] is ')' or ']')
            {
                var close = text[i];
                var open = close == ')' ? '(' : '[';
                var depth = 1;
                i--;
                while (i >= 0 && depth > 0)
                {
                    if (text[i] == close) depth++;
                    else if (text[i] == open) depth--;
                    i--;
                }

                if (depth > 0)
                {
                    return null;
                }

                if (close == ')') hasCall = true;
                else hasIndex = true;
            }

            var end = i + 1;
            var start = end;
            while (start > 0 && IsIdentChar(text[start - 1]))
            {
                start--;
            }

            if (end <= start)
            {
                return null;
            }

            segs.Add(new Segment(text[start..end], hasCall, hasIndex));
            if (start > 0 && text[start - 1] == '.')
            {
                i = start - 1;
                continue;
            }

            segs.Reverse();
            return segs;
        }
    }

    private readonly record struct Resolved(Type Type, bool IsStatic);

    private static Resolved ResolveChain(List<Segment> chain, int syntaxVersion, Type lambdaElement)
    {
        Type current = null;
        var isStatic = false;
        var startIndex = 0;
        var first = chain[0];

        if (syntaxVersion == 2 && string.Equals(first.Name, "State", StringComparison.Ordinal) && !first.HasCall && !first.HasIndex)
        {
            current = typeof(RuleState);
            startIndex = 1;
        }
        else
        {
            // v1 binds the whole lambda to RuleState; inside a LINQ lambda the element type wins first.
            if (lambdaElement != null)
            {
                current = StepInto(lambdaElement, first);
            }

            if (current == null && syntaxVersion == 1)
            {
                current = StepInto(typeof(RuleState), first);
            }

            if (current == null && TypeMap.TryGetValue(first.Name, out var staticType) && !first.HasCall && !first.HasIndex)
            {
                current = staticType;
                isStatic = true;
            }

            if (current == null)
            {
                return default;
            }

            startIndex = 1;
        }

        for (var i = startIndex; i < chain.Count; i++)
        {
            if (isStatic)
            {
                // Enum value or static member access: MonsterRarity.Rare.<...> — resolve and go instance.
                current = current.IsEnum && !chain[i].HasCall && !chain[i].HasIndex
                    ? current
                    : StepIntoStatic(current, chain[i]);
                isStatic = false;
            }
            else
            {
                current = StepInto(current, chain[i]);
            }

            if (current == null)
            {
                return default;
            }
        }

        return new Resolved(current, isStatic);
    }

    private static Type StepInto(Type type, Segment seg)
    {
        if (type == null)
        {
            return null;
        }

        Type result = null;
        if (seg.HasCall)
        {
            var method = type.GetMethods(BindingFlags.Public | BindingFlags.Instance)
                .FirstOrDefault(m => !m.IsSpecialName && m.Name.Equals(seg.Name, StringComparison.OrdinalIgnoreCase));
            if (method != null)
            {
                result = method.ReturnType;
            }
            else if (TryGetEnumerableElement(type, out var element) && LinqMethods.TryGetValue(seg.Name, out var map))
            {
                result = map(element);
            }
        }
        else
        {
            var prop = type.GetProperty(seg.Name,
                BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
            result = prop?.PropertyType ?? type.GetField(seg.Name,
                BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase)?.FieldType;
        }

        if (result == null)
        {
            return null;
        }

        if (seg.HasIndex)
        {
            result = IndexerResultOf(result);
        }

        return result;
    }

    private static Type StepIntoStatic(Type type, Segment seg)
    {
        if (seg.HasCall)
        {
            return type.GetMethods(BindingFlags.Public | BindingFlags.Static)
                .FirstOrDefault(m => m.Name.Equals(seg.Name, StringComparison.OrdinalIgnoreCase))?.ReturnType;
        }

        var prop = type.GetProperty(seg.Name, BindingFlags.Public | BindingFlags.Static | BindingFlags.IgnoreCase);
        if (prop != null)
        {
            return prop.PropertyType;
        }

        var field = type.GetField(seg.Name, BindingFlags.Public | BindingFlags.Static | BindingFlags.IgnoreCase);
        if (field != null)
        {
            return field.FieldType;
        }

        return type.IsEnum && Enum.GetNames(type).Any(n => n.Equals(seg.Name, StringComparison.OrdinalIgnoreCase))
            ? type
            : null;
    }

    private static Type IndexerResultOf(Type type)
    {
        if (type.IsArray)
        {
            return type.GetElementType();
        }

        var indexer = type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .FirstOrDefault(p => p.GetIndexParameters().Length > 0);
        if (indexer != null)
        {
            return indexer.PropertyType;
        }

        return TryGetEnumerableElement(type, out var element) ? element : null;
    }

    private static bool TryGetEnumerableElement(Type type, out Type element)
    {
        element = null;
        if (type == typeof(string))
        {
            return false;
        }

        var ienum = type.IsGenericType && type.GetGenericTypeDefinition() == typeof(IEnumerable<>)
            ? type
            : type.GetInterfaces().FirstOrDefault(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IEnumerable<>));
        element = ienum?.GetGenericArguments()[0];
        return element != null;
    }

    /// <summary>
    /// If the caret sits inside `<chain>.<LinqMethod>(`, returns the element type of the chain so
    /// the inner expression completes against it (Dynamic LINQ binds lambda bodies that way).
    /// </summary>
    private static Type FindLambdaElement(string text, List<int> openParens, RuleState state)
    {
        for (var p = openParens.Count - 1; p >= 0; p--)
        {
            var parenPos = openParens[p];
            var end = parenPos;
            var start = end;
            while (start > 0 && IsIdentChar(text[start - 1]))
            {
                start--;
            }

            if (end <= start || !LambdaMethods.Contains(text[start..end]))
            {
                continue;
            }

            if (start == 0 || text[start - 1] != '.')
            {
                continue;
            }

            var ownerChain = ParseChainBackward(text, start - 1);
            if (ownerChain == null)
            {
                continue;
            }

            var owner = ResolveChain(ownerChain, 1, null);
            if (owner.Type != null && TryGetEnumerableElement(owner.Type, out var element))
            {
                return element;
            }
        }

        return null;
    }

    private static List<CompletionItem> RootCompletions(string text, int tokenStart, int syntaxVersion, RuleActionType actionType, Type lambdaElement)
    {
        // `new ` context: constructible side-effect types.
        var wordEnd = tokenStart;
        while (wordEnd > 0 && char.IsWhiteSpace(text[wordEnd - 1]))
        {
            wordEnd--;
        }

        var wordStart = wordEnd;
        while (wordStart > 0 && IsIdentChar(text[wordStart - 1]))
        {
            wordStart--;
        }

        if (wordEnd > wordStart && text[wordStart..wordEnd] == "new")
        {
            return ConstructibleTypes();
        }

        var pool = new List<CompletionItem>();
        if (lambdaElement != null)
        {
            pool.AddRange(InstanceMembersOf(lambdaElement));
        }

        if (syntaxVersion == 1)
        {
            var stateRank = lambdaElement != null ? 10 : 0;
            pool.AddRange(InstanceMembersOf(typeof(RuleState)).Select(i => i with { Rank = i.Rank + stateRank }));
        }
        else
        {
            pool.Add(new CompletionItem("State", "State", "RuleState", 0));
            // v2 expressions start from the State global; offering `State.X` directly lets users
            // type the member name they know without remembering the prefix.
            pool.AddRange(InstanceMembersOf(typeof(RuleState))
                .Select(i => i with { Label = $"State.{i.Label}", InsertText = $"State.{i.InsertText}", Rank = i.Rank + 2 }));
        }

        pool.Add(new CompletionItem("true", "true", "", 5));
        pool.Add(new CompletionItem("false", "false", "", 5));
        pool.Add(new CompletionItem("null", "null", "", 5));
        if (actionType != RuleActionType.Key)
        {
            pool.Add(new CompletionItem("new", "new ", "create a side effect", 5));
        }

        pool.AddRange(TypeMap.Values.Distinct()
            .Where(t => t.IsEnum)
            .Select(t => new CompletionItem(t.Name, t.Name, "enum", 20)));
        return pool;
    }

    private static List<CompletionItem> ConstructibleTypes()
    {
        return TypeCache(typeof(CompletionEngine), "new", static () =>
            typeof(CompletionEngine).Assembly.GetExportedTypes()
                .Where(t => !t.IsAbstract && !t.IsInterface && !t.IsEnum &&
                            t.GetCustomAttributes().Any(a => a.GetType().Name == nameof(DynamicLinqTypeAttribute)))
                .Where(t => t.GetConstructors().Any(c => c.GetParameters().Length > 0))
                .OrderBy(t => t.Name)
                .Select(t =>
                {
                    var ctor = t.GetConstructors().OrderByDescending(c => c.GetParameters().Length).First();
                    return new CompletionItem(t.Name, $"{t.Name}(", $"({ParamList(ctor.GetParameters())})", 0);
                })
                .ToList());
    }

    private static readonly Dictionary<(Type, string), List<CompletionItem>> KeyedCache = [];

    private static List<CompletionItem> TypeCache(Type key, string tag, Func<List<CompletionItem>> build)
    {
        lock (KeyedCache)
        {
            if (!KeyedCache.TryGetValue((key, tag), out var items))
            {
                KeyedCache[(key, tag)] = items = build();
            }

            return items;
        }
    }

    private static List<CompletionItem> InstanceMembersOf(Type type)
    {
        lock (MemberCache)
        {
            if (MemberCache.TryGetValue(type, out var cached))
            {
                return cached;
            }
        }

        var items = BuildInstanceMembers(type);
        lock (MemberCache)
        {
            MemberCache[type] = items;
        }

        return items;
    }

    private static readonly HashSet<string> NoiseMethods =
    [
        "ToString", "GetHashCode", "Equals", "GetType", "CompareTo", "Deconstruct", "GetEnumerator", "PrintMembers", "<Clone>$",
    ];

    private static List<CompletionItem> BuildInstanceMembers(Type type)
    {
        var items = new List<CompletionItem>();
        if (type == null || type.IsPrimitive || type == typeof(string))
        {
            return items;
        }

        var props = type.GetProperties(BindingFlags.Public | BindingFlags.Instance).Where(p => p.GetIndexParameters().Length == 0).ToList();
        var fields = type.GetFields(BindingFlags.Public | BindingFlags.Instance).ToList();
        var methods = type.GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .Where(m => !m.IsSpecialName && m.DeclaringType != typeof(object) && !NoiseMethods.Contains(m.Name))
            .ToList();

        // When a type marks any member [Api], that attribute is its intended rule-facing surface.
        var apiOnly = props.Any(HasApi) || methods.Any(HasApi);
        if (apiOnly)
        {
            props = props.Where(HasApi).ToList();
            fields = fields.Where(HasApi).ToList();
            methods = methods.Where(HasApi).ToList();
        }

        items.AddRange(props.Select(p => new CompletionItem(p.Name, p.Name, ShortName(p.PropertyType), 0)));
        items.AddRange(fields.Select(f => new CompletionItem(f.Name, f.Name, ShortName(f.FieldType), 0)));
        items.AddRange(methods.GroupBy(m => m.Name).Select(g =>
        {
            var m = g.OrderBy(x => x.GetParameters().Length).First();
            var overloads = g.Count() > 1 ? $" +{g.Count() - 1}" : "";
            var insert = m.GetParameters().Length == 0 && g.Count() == 1 ? $"{m.Name}()" : $"{m.Name}(";
            return new CompletionItem(m.Name, insert, $"({ParamList(m.GetParameters())}) → {ShortName(m.ReturnType)}{overloads}", 0);
        }));

        var indexer = type.GetProperties(BindingFlags.Public | BindingFlags.Instance).FirstOrDefault(p => p.GetIndexParameters().Length > 0);
        if (indexer != null)
        {
            items.Add(new CompletionItem("[…]", "[", $"[{ParamList(indexer.GetIndexParameters())}] → {ShortName(indexer.PropertyType)}", 1));
        }

        if (TryGetEnumerableElement(type, out var element))
        {
            var existing = items.Select(i => i.Label).ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var (name, map) in LinqMethods)
            {
                if (existing.Contains(name))
                {
                    continue;
                }

                var ret = ShortName(map(element));
                var needsArg = LambdaMethods.Contains(name) && name is not ("First" or "FirstOrDefault" or "Count" or "Any");
                items.Add(new CompletionItem(name, $"{name}(", needsArg ? $"(condition) → {ret}" : $"(…) → {ret}", 2));
            }
        }

        return items.OrderBy(i => i.Rank).ThenBy(i => i.Label, StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static List<CompletionItem> StaticMembersOf(Type type)
    {
        return TypeCache(type, "static", () =>
        {
            if (type.IsEnum)
            {
                string[] names;
                lock (EnumNameCache)
                {
                    if (!EnumNameCache.TryGetValue(type, out names))
                    {
                        EnumNameCache[type] = names = Enum.GetNames(type);
                    }
                }

                return names.Select(n => new CompletionItem(n, n, type.Name, 0)).ToList();
            }

            var items = new List<CompletionItem>();
            items.AddRange(type.GetProperties(BindingFlags.Public | BindingFlags.Static)
                .Select(p => new CompletionItem(p.Name, p.Name, ShortName(p.PropertyType), 0)));
            items.AddRange(type.GetFields(BindingFlags.Public | BindingFlags.Static)
                .Select(f => new CompletionItem(f.Name, f.Name, ShortName(f.FieldType), 0)));
            return items.OrderBy(i => i.Label, StringComparer.OrdinalIgnoreCase).ToList();
        });
    }

    private static bool HasApi(MemberInfo member) => member.GetCustomAttribute<ApiAttribute>() != null;

    private static Dictionary<string, Type> TypeMap => _typeMap ??= BuildTypeMap();

    private static Dictionary<string, Type> BuildTypeMap()
    {
        var map = new Dictionary<string, Type>(StringComparer.OrdinalIgnoreCase);
        foreach (var t in new[] { typeof(State.MonsterRarity), typeof(GameStat), typeof(Keys), typeof(AnimationE), typeof(Vector2), typeof(Vector3) })
        {
            map[t.Name] = t;
        }

        foreach (var t in typeof(CompletionEngine).Assembly.GetExportedTypes().Where(t => t.IsEnum))
        {
            map.TryAdd(t.Name, t);
        }

        return map;
    }

    /// <summary>
    /// Completions inside a string literal: live buff/skill names read from the running game, and
    /// flag/timer/number names already set in the current group.
    /// </summary>
    private enum LiveSource
    {
        None,
        BuffOrSkillDictionary,
        Ailments,
        Players,
        Flags,
        Numbers,
        Timers,
    }

    private static CompletionResult StringContextCompletions(string text, int caret, int stringStart, int syntaxVersion, RuleState state)
    {
        if (stringStart < 0)
        {
            return CompletionResult.Empty;
        }

        var prefix = text[(stringStart + 1)..caret];
        var closeQuote = caret >= text.Length || text[caret] != '"';

        // What owns this string? Either `Chain.Method("` or `Chain["`.
        var pos = stringStart;
        while (pos > 0 && char.IsWhiteSpace(text[pos - 1]))
        {
            pos--;
        }

        if (pos == 0)
        {
            return CompletionResult.Empty;
        }

        var source = LiveSource.None;
        var identStart = 0;
        var identEnd = 0;
        var opener = text[pos - 1];
        if (opener is '(' or '[')
        {
            var chainEnd = pos - 1;
            while (chainEnd > 0 && char.IsWhiteSpace(text[chainEnd - 1]))
            {
                chainEnd--;
            }

            identEnd = chainEnd;
            identStart = identEnd;
            while (identStart > 0 && IsIdentChar(text[identStart - 1]))
            {
                identStart--;
            }

            var lastIdent = identEnd > identStart ? text[identStart..identEnd] : null;
            if (opener == '[')
            {
                // `<chain>["` — indexer key on the resolved chain type.
                var ownerType = ResolveOwner(text, identStart, identEnd, syntaxVersion);
                if (ownerType == typeof(BuffDictionary) || ownerType == typeof(SkillDictionary))
                {
                    source = LiveSource.BuffOrSkillDictionary;
                }
            }
            else if (lastIdent != null)
            {
                source = lastIdent switch
                {
                    "Has" when ResolveOwnerOfMethod(text, identStart, syntaxVersion) is { } t &&
                               (t == typeof(BuffDictionary) || t == typeof(SkillDictionary)) => LiveSource.BuffOrSkillDictionary,
                    "Contains" when ResolveOwnerOfMethod(text, identStart, syntaxVersion) == typeof(IReadOnlyCollection<string>) => LiveSource.Ailments,
                    "PlayerByName" => LiveSource.Players,
                    "IsFlagSet" or "SetFlagSideEffect" or "ResetFlagSideEffect" => LiveSource.Flags,
                    "GetNumberValue" or "SetNumberSideEffect" or "ResetNumberSideEffect" => LiveSource.Numbers,
                    "GetTimerValue" or "IsTimerRunning" or "StartTimerSideEffect" or "StopTimerSideEffect"
                        or "RestartTimerSideEffect" or "ResetTimerSideEffect" => LiveSource.Timers,
                    _ => LiveSource.None,
                };
            }
        }

        if (source == LiveSource.None)
        {
            return CompletionResult.Empty;
        }

        // Ailment candidates come from CustomAilments.json, not the live state.
        if (source == LiveSource.Ailments)
        {
            return NameItems(CustomAilmentNames.ToList(), prefix, stringStart, closeQuote, "ailment");
        }

        // A recognized live-data spot with nothing to read (not fully in game) gets an explanatory
        // hint instead of silence; inserting it is a no-op replace of the typed prefix.
        if (state == null)
        {
            return new CompletionResult
            {
                Items = [new CompletionItem("(no live game data — log fully into a character)", prefix, "", 0)],
                ReplaceStart = stringStart + 1,
                AutoShow = true,
            };
        }

        var (names, detail) = source switch
        {
            LiveSource.BuffOrSkillDictionary => (LiveNamesFor(
                opener == '[' ? ResolveOwner(text, identStart, identEnd, syntaxVersion) : ResolveOwnerOfMethod(text, identStart, syntaxVersion),
                state, text, identStart, identEnd, syntaxVersion), "live"),
            LiveSource.Players => (PlayerNames(state), "player"),
            LiveSource.Flags => (GroupStateNames(state, s => s.Flags.Keys), "flag"),
            LiveSource.Numbers => (GroupStateNames(state, s => s.Numbers.Keys), "number"),
            LiveSource.Timers => (GroupStateNames(state, s => s.Timers.Keys), "timer"),
            _ => (null, ""),
        };

        return names == null || names.Count == 0
            ? CompletionResult.Empty
            : NameItems(names, prefix, stringStart, closeQuote, detail);
    }

    private static CompletionResult NameItems(List<string> names, string prefix, int stringStart, bool closeQuote, string detail)
    {
        var items = names
            .Where(n => !string.IsNullOrEmpty(n))
            .Distinct()
            .Where(n => n.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) ||
                        (prefix.Length >= 2 && n.Contains(prefix, StringComparison.OrdinalIgnoreCase)))
            .OrderBy(n => !n.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            .ThenBy(n => n, StringComparer.OrdinalIgnoreCase)
            .Take(MaxItems)
            .Select(n => new CompletionItem(n, closeQuote ? $"{n}\"" : n, detail, 0))
            .ToList();

        return new CompletionResult { Items = items, ReplaceStart = stringStart + 1, AutoShow = items.Count > 0 };
    }

    private static List<string> PlayerNames(RuleState state)
    {
        try
        {
            return state.AllPlayers.Select(p => p.PlayerName).ToList();
        }
        catch
        {
            return null;
        }
    }

    private static Type ResolveOwner(string text, int identStart, int identEnd, int syntaxVersion)
    {
        if (identEnd <= identStart)
        {
            return null;
        }

        var seg = new Segment(text[identStart..identEnd], false, false);
        if (identStart > 0 && text[identStart - 1] == '.')
        {
            var chain = ParseChainBackward(text, identStart - 1);
            if (chain == null)
            {
                return null;
            }

            chain.Add(seg);
            return ResolveChain(chain, syntaxVersion, null).Type;
        }

        return ResolveChain([seg], syntaxVersion, null).Type;
    }

    private static Type ResolveOwnerOfMethod(string text, int methodStart, int syntaxVersion)
    {
        // `<chain>.Has("` — the owner is the chain before the method.
        if (methodStart == 0 || text[methodStart - 1] != '.')
        {
            return null;
        }

        var chain = ParseChainBackward(text, methodStart - 1);
        return chain == null ? null : ResolveChain(chain, syntaxVersion, null).Type;
    }

    private static List<string> LiveNamesFor(Type ownerType, RuleState state, string text, int identStart, int identEnd, int syntaxVersion)
    {
        if (ownerType == typeof(BuffDictionary))
        {
            var dict = ResolveLiveInstance(state, text, identStart, identEnd, syntaxVersion) as BuffDictionary ?? state.Buffs;
            return dict?.AllBuffs?.Select(b => b.Name).ToList();
        }

        if (ownerType == typeof(SkillDictionary))
        {
            var dict = ResolveLiveInstance(state, text, identStart, identEnd, syntaxVersion) as SkillDictionary ?? state.Skills;
            return dict?.AllSkills?.Select(s => s.Name).ToList();
        }

        return null;
    }

    /// <summary>
    /// Walks the actual live state instance along a plain property chain (no calls/indexers) so
    /// `Player.Buffs["` completes that entity's buffs rather than the player's. Bounded and best-effort.
    /// </summary>
    private static object ResolveLiveInstance(RuleState state, string text, int identStart, int identEnd, int syntaxVersion)
    {
        try
        {
            var segs = new List<Segment>();
            if (identStart > 0 && text[identStart - 1] == '.')
            {
                segs = ParseChainBackward(text, identStart - 1);
                if (segs == null)
                {
                    return null;
                }
            }

            segs.Add(new Segment(text[identStart..identEnd], false, false));
            if (syntaxVersion == 2)
            {
                if (segs.Count == 0 || segs[0].Name != "State")
                {
                    return null;
                }

                segs.RemoveAt(0);
            }

            if (segs.Count > 4 || segs.Any(s => s.HasCall || s.HasIndex))
            {
                return null;
            }

            object current = state;
            foreach (var seg in segs)
            {
                var prop = current?.GetType().GetProperty(seg.Name,
                    BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
                current = prop?.GetValue(current);
                if (current == null)
                {
                    return null;
                }
            }

            return current;
        }
        catch
        {
            return null;
        }
    }

    private static List<string> GroupStateNames(RuleState state, Func<PerGroupInternalState, IEnumerable<string>> selector)
    {
        try
        {
            var group = state.InternalState.CurrentGroupState;
            return group == null ? null : selector(group).ToList();
        }
        catch
        {
            return null;
        }
    }

    private static List<CompletionItem> Filter(List<CompletionItem> pool, string prefix)
    {
        IEnumerable<CompletionItem> matches;
        if (prefix.Length == 0)
        {
            matches = pool;
        }
        else
        {
            matches = pool.Where(i => i.Label.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
            if (prefix.Length >= 2)
            {
                var starts = matches.ToList();
                var startLabels = starts.Select(i => i.Label).ToHashSet(StringComparer.OrdinalIgnoreCase);
                matches = starts.Concat(pool.Where(i =>
                    !startLabels.Contains(i.Label) &&
                    i.Label.Contains(prefix, StringComparison.OrdinalIgnoreCase)).Select(i => i with { Rank = i.Rank + 30 }));
            }
        }

        return matches
            .OrderBy(i => i.Rank)
            .ThenBy(i => i.Label, StringComparer.OrdinalIgnoreCase)
            .Take(MaxItems)
            .ToList();
    }

    private static string ParamList(ParameterInfo[] parameters) =>
        string.Join(", ", parameters.Select(p => $"{ShortName(p.ParameterType)} {p.Name}"));

    private static string ShortName(Type t)
    {
        if (t == null)
        {
            return "?";
        }

        if (t == typeof(bool)) return "bool";
        if (t == typeof(int)) return "int";
        if (t == typeof(float)) return "float";
        if (t == typeof(double)) return "double";
        if (t == typeof(string)) return "string";
        if (t == typeof(void)) return "void";
        var nullable = Nullable.GetUnderlyingType(t);
        if (nullable != null)
        {
            return $"{ShortName(nullable)}?";
        }

        if (t.IsArray)
        {
            return $"{ShortName(t.GetElementType())}[]";
        }

        if (t.IsGenericType)
        {
            if (TryGetEnumerableElement(t, out var element) && t != typeof(string))
            {
                return $"{ShortName(element)}[]";
            }

            var name = t.Name;
            var tick = name.IndexOf('`');
            if (tick > 0)
            {
                name = name[..tick];
            }

            return $"{name}<{string.Join(", ", t.GetGenericArguments().Select(ShortName))}>";
        }

        return t.Name;
    }
}
