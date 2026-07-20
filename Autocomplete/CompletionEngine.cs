using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Dynamic.Core.CustomTypeProviders;
using System.Numerics;
using System.Reflection;
using System.Windows.Forms;
using ExileCore;
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

    /// <summary>Set at plugin init; used to read the game's full buff-definition list.</summary>
    public static GameController GameController { get; set; }

    private static IReadOnlyList<string> _allBuffIds;

    // Members most rules start with, surfaced first at the root.
    private static readonly HashSet<string> CommonRoots = new(StringComparer.Ordinal)
    {
        "SinceLastActivation", "Vitals", "Flasks", "Buffs", "Skills", "MonsterCount", "Monsters", "IsMoving", "IsKeyPressed",
    };

    private static readonly string[] Operators = ["&&", "||", "==", "!=", ">=", "<=", ">", "<"];

    /// <summary>
    /// Every buff id in the game's data files (~2700 entries). Read once and cached; a failed read
    /// (e.g. between areas) just returns empty and retries on the next request.
    /// </summary>
    private static IReadOnlyList<string> AllKnownBuffIds()
    {
        if (_allBuffIds != null)
        {
            return _allBuffIds;
        }

        try
        {
            var list = GameController?.Files?.BuffDefinitions?.EntriesList?
                .Select(b => b?.Id)
                .Where(id => !string.IsNullOrEmpty(id))
                .Distinct()
                .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (list is { Count: > 0 })
            {
                _allBuffIds = list;
                return list;
            }
        }
        catch
        {
            // Fall through — retry next request.
        }

        return [];
    }

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

        if (tokenStart > 0 && text[tokenStart - 1] == '[')
        {
            return BracketCompletions(text, tokenStart, prefix, syntaxVersion, state, caret);
        }

        // Numeric literal like `50` — never complete inside it.
        if (prefix.Length > 0 && char.IsDigit(prefix[0]))
        {
            return CompletionResult.Empty;
        }

        var afterDot = tokenStart > 0 && text[tokenStart - 1] == '.';
        var lambdaElement = syntaxVersion == 1 ? FindLambdaElement(text, scan.OpenParens, state) : null;

        if (prefix.Length == 0 && !afterDot && tokenStart > 0 && text[tokenStart - 1] == ' ')
        {
            var spaceResult = SpaceContextCompletions(text, tokenStart, syntaxVersion, actionType, lambdaElement);
            if (spaceResult != null)
            {
                return spaceResult;
            }
        }

        List<CompletionItem> pool;
        Type contextType;
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

            contextType = resolved.IsStatic ? null : resolved.Type;
            pool = resolved.IsStatic ? StaticMembersOf(resolved.Type) : InstanceMembersOf(resolved.Type);
        }
        else
        {
            contextType = lambdaElement ?? (syntaxVersion == 1 ? typeof(RuleState) : null);
            pool = RootCompletions(text, tokenStart, syntaxVersion, actionType, lambdaElement);
        }

        var items = Filter(pool, prefix);

        // A fully-typed name: offer its continuations (`.`, `[`, method `(`) instead of hiding.
        if (prefix.Length > 0 && items.Count == 1 && string.Equals(items[0].Label, prefix, StringComparison.Ordinal))
        {
            var continuations = ContinuationsFor(prefix, contextType, syntaxVersion, afterDot, items[0]);
            return new CompletionResult { Items = continuations, ReplaceStart = tokenStart, AutoShow = continuations.Count > 0 };
        }

        var autoShow = items.Count > 0 && (prefix.Length > 0 || afterDot);
        return new CompletionResult { Items = items, ReplaceStart = tokenStart, AutoShow = autoShow };
    }

    /// <summary>
    /// After a space: operators when a value just ended, root members when an operator did.
    /// Returns null to fall through to normal handling (e.g. after `new `).
    /// </summary>
    private static CompletionResult SpaceContextCompletions(string text, int tokenStart, int syntaxVersion, RuleActionType actionType, Type lambdaElement)
    {
        var q = tokenStart;
        while (q > 0 && text[q - 1] == ' ')
        {
            q--;
        }

        if (q == 0)
        {
            return null;
        }

        var wordEnd = q;
        var wordStart = wordEnd;
        while (wordStart > 0 && IsIdentChar(text[wordStart - 1]))
        {
            wordStart--;
        }

        if (wordEnd > wordStart && text[wordStart..wordEnd] == "new")
        {
            return null;
        }

        var prev = text[q - 1];
        if (IsIdentChar(prev) || prev is ')' or ']' or '"')
        {
            var items = Operators.Select((o, i) => new CompletionItem(o, $"{o} ", "", i)).ToList();
            return new CompletionResult { Items = items, ReplaceStart = tokenStart, AutoShow = true };
        }

        if (prev is '&' or '|' or '=' or '<' or '>' or '!' or '(' or ',' or '+' or '-' or '*' or '/')
        {
            var pool = RootCompletions(text, tokenStart, syntaxVersion, actionType, lambdaElement);
            return new CompletionResult { Items = Filter(pool, ""), ReplaceStart = tokenStart, AutoShow = true };
        }

        return null;
    }

    private static List<CompletionItem> ContinuationsFor(string name, Type contextType, int syntaxVersion, bool afterDot, CompletionItem exactItem)
    {
        if (!string.Equals(exactItem.InsertText, name, StringComparison.Ordinal))
        {
            return [exactItem];   // methods keep offering their `(` insert
        }

        Type memberType = null;
        if (syntaxVersion == 2 && !afterDot && string.Equals(name, "State", StringComparison.Ordinal))
        {
            memberType = typeof(RuleState);
        }
        else if (contextType != null)
        {
            memberType = StepInto(contextType, new Segment(name, false, false));
        }

        if (memberType == null)
        {
            return [];
        }

        var result = new List<CompletionItem>();
        if (InstanceMembersOf(memberType).Count > 0)
        {
            result.Add(new CompletionItem($"{name}.", $"{name}.", "members", 0));
        }

        var indexer = memberType.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .FirstOrDefault(p => p.GetIndexParameters().Length > 0);
        if (indexer != null)
        {
            result.Add(new CompletionItem($"{name}[", $"{name}[",
                $"[{ParamList(indexer.GetIndexParameters())}] → {ShortName(indexer.PropertyType)}", 1));
        }

        return result;
    }

    /// <summary>
    /// Directly after `[`: flask slot numbers (with what's in each slot), buff/skill names as
    /// quoted keys, or the enum-type prefix for enum-keyed dictionaries like MapStats.
    /// </summary>
    private static CompletionResult BracketCompletions(string text, int tokenStart, string prefix, int syntaxVersion, RuleState state, int caret)
    {
        var identEnd = tokenStart - 1;
        while (identEnd > 0 && char.IsWhiteSpace(text[identEnd - 1]))
        {
            identEnd--;
        }

        var identStart = identEnd;
        while (identStart > 0 && IsIdentChar(text[identStart - 1]))
        {
            identStart--;
        }

        if (identEnd <= identStart)
        {
            return CompletionResult.Empty;
        }

        var ownerType = ResolveOwner(text, identStart, identEnd, syntaxVersion);
        var indexer = ownerType?.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .FirstOrDefault(p => p.GetIndexParameters().Length > 0);
        if (indexer == null)
        {
            return CompletionResult.Empty;
        }

        var closeBracket = caret >= text.Length || text[caret] != ']';
        var paramType = indexer.GetIndexParameters()[0].ParameterType;

        if (paramType == typeof(string))
        {
            if (state == null)
            {
                return HintResult(prefix, tokenStart);
            }

            return BuildNameResult(
                DictionaryNameCandidates(ownerType, state, text, identStart, identEnd, syntaxVersion, prefix),
                prefix, tokenStart,
                n => closeBracket ? $"\"{n}\"]" : $"\"{n}\"");
        }

        if (paramType == typeof(int))
        {
            var slots = ownerType == typeof(FlasksInfo) ? 5 : 3;
            var items = Enumerable.Range(0, slots)
                .Where(i => prefix.Length == 0 || i.ToString().StartsWith(prefix, StringComparison.Ordinal))
                .Select(i => new CompletionItem(i.ToString(), closeBracket ? $"{i}]" : i.ToString(),
                    ownerType == typeof(FlasksInfo) ? FlaskSlotDetail(state, i) : "", i))
                .ToList();
            return new CompletionResult { Items = items, ReplaceStart = tokenStart, AutoShow = prefix.Length == 0 && items.Count > 0 };
        }

        if (paramType.IsEnum)
        {
            if (prefix.Length > 0 && !paramType.Name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                return CompletionResult.Empty;
            }

            return new CompletionResult
            {
                Items = [new CompletionItem($"{paramType.Name}.", $"{paramType.Name}.", "enum", 0)],
                ReplaceStart = tokenStart,
                AutoShow = true,
            };
        }

        return CompletionResult.Empty;
    }

    private static string FlaskSlotDetail(RuleState state, int slot)
    {
        try
        {
            var flask = slot switch
            {
                0 => state?.Flasks?.Flask1,
                1 => state?.Flasks?.Flask2,
                2 => state?.Flasks?.Flask3,
                3 => state?.Flasks?.Flask4,
                4 => state?.Flasks?.Flask5,
                _ => null,
            };
            return string.IsNullOrEmpty(flask?.Name) ? "" : flask.Name;
        }
        catch
        {
            return "";
        }
    }

    private static CompletionResult HintResult(string prefix, int replaceStart) => new()
    {
        Items = [new CompletionItem("(no live game data — log fully into a character)", prefix, "", 0)],
        ReplaceStart = replaceStart,
        AutoShow = true,
    };

    private static bool IsSubsequence(string needle, string hay)
    {
        var i = 0;
        foreach (var c in hay)
        {
            if (i < needle.Length && char.ToLowerInvariant(c) == char.ToLowerInvariant(needle[i]))
            {
                i++;
            }
        }

        return i >= needle.Length;
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

        for (var i = 0; i < pool.Count; i++)
        {
            if (CommonRoots.Contains(pool[i].Label))
            {
                pool[i] = pool[i] with { Rank = -1 };
            }
        }

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
            return BuildNameResult(CustomAilmentNames.Select(n => (n, "ailment", 0)), prefix, stringStart + 1,
                n => closeQuote ? $"{n}\"" : n);
        }

        // A recognized live-data spot with nothing to read (not fully in game) gets an explanatory
        // hint instead of silence; inserting it is a no-op replace of the typed prefix.
        if (state == null)
        {
            return HintResult(prefix, stringStart + 1);
        }

        IEnumerable<(string Name, string Detail, int Rank)> candidates = source switch
        {
            LiveSource.BuffOrSkillDictionary => DictionaryNameCandidates(
                opener == '[' ? ResolveOwner(text, identStart, identEnd, syntaxVersion) : ResolveOwnerOfMethod(text, identStart, syntaxVersion),
                state, text, identStart, identEnd, syntaxVersion, prefix),
            LiveSource.Players => (PlayerNames(state) ?? []).Select(n => (n, "player", 0)),
            LiveSource.Flags => (GroupStateNames(state, s => s.Flags.Keys) ?? []).Select(n => (n, "flag", 0)),
            LiveSource.Numbers => (GroupStateNames(state, s => s.Numbers.Keys) ?? []).Select(n => (n, "number", 0)),
            LiveSource.Timers => (GroupStateNames(state, s => s.Timers.Keys) ?? []).Select(n => (n, "timer", 0)),
            _ => [],
        };

        return BuildNameResult(candidates, prefix, stringStart + 1, n => closeQuote ? $"{n}\"" : n);
    }

    /// <summary>
    /// Active buff/skill names first; for buffs with 2+ typed characters, the full game buff
    /// catalog joins in ranked below the active set.
    /// </summary>
    private static IEnumerable<(string Name, string Detail, int Rank)> DictionaryNameCandidates(
        Type ownerType, RuleState state, string text, int identStart, int identEnd, int syntaxVersion, string prefix)
    {
        var active = LiveNamesFor(ownerType, state, text, identStart, identEnd, syntaxVersion) ?? [];
        foreach (var name in active)
        {
            yield return (name, "active", 0);
        }

        if (ownerType == typeof(BuffDictionary) && prefix.Length >= 2)
        {
            var activeSet = active.ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var id in AllKnownBuffIds())
            {
                if (!activeSet.Contains(id))
                {
                    yield return (id, "known", 5);
                }
            }
        }
    }

    private static CompletionResult BuildNameResult(
        IEnumerable<(string Name, string Detail, int Rank)> candidates, string prefix, int replaceStart, Func<string, string> makeInsert)
    {
        var items = new List<CompletionItem>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (name, detail, rank) in candidates)
        {
            if (string.IsNullOrEmpty(name) || !seen.Add(name))
            {
                continue;
            }

            int tier;
            if (prefix.Length == 0 || name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                tier = 0;
            }
            else if (prefix.Length >= 2 && name.Contains(prefix, StringComparison.OrdinalIgnoreCase))
            {
                tier = 30;
            }
            else if (prefix.Length >= 3 && IsSubsequence(prefix, name))
            {
                tier = 60;
            }
            else
            {
                continue;
            }

            items.Add(new CompletionItem(name, makeInsert(name), detail, rank + tier));
        }

        items = items.OrderBy(i => i.Rank).ThenBy(i => i.Label, StringComparer.OrdinalIgnoreCase).Take(MaxItems).ToList();
        return new CompletionResult { Items = items, ReplaceStart = replaceStart, AutoShow = items.Count > 0 };
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
            var starts = pool.Where(i => i.Label.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)).ToList();
            var seen = starts.Select(i => i.Label).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var extra = new List<CompletionItem>();
            if (prefix.Length >= 2)
            {
                foreach (var i in pool)
                {
                    if (!seen.Contains(i.Label) && i.Label.Contains(prefix, StringComparison.OrdinalIgnoreCase))
                    {
                        seen.Add(i.Label);
                        extra.Add(i with { Rank = i.Rank + 30 });
                    }
                }
            }

            if (prefix.Length >= 3)
            {
                foreach (var i in pool)
                {
                    if (!seen.Contains(i.Label) && IsSubsequence(prefix, i.Label))
                    {
                        seen.Add(i.Label);
                        extra.Add(i with { Rank = i.Rank + 60 });
                    }
                }
            }

            matches = starts.Concat(extra);
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
