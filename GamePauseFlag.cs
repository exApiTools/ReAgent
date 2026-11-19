using System;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using ExileCore.Shared.Interfaces;

namespace ReAgent;

/// <summary>
/// Finds Path of Exile's global "game is paused" byte (1 = paused, 0 = running).
/// ExileCore doesn't expose it: EscapeState is the Escape (options) menu, which is separate from
/// pausing, so IsEscapeState stays false while the game is paused.
/// The byte's address changes with every game patch, so instead of
/// hard-coding it, this searches the game's code for instructions that read or write it and
/// takes the address from them. The search runs once per game process, in the background.
/// </summary>
internal sealed class GamePauseFlag
{
    private sealed record Signature(string Name, byte?[] Bytes, int DisplacementOffset, int InstructionEnd);

    // Each signature matches code that accesses the flag through a RIP-relative address.
    // DisplacementOffset is where that address's 32-bit displacement starts in the match, and
    // InstructionEnd is where the instruction ends (the displacement is relative to it).
    private static readonly Signature[] Signatures =
    [
        // movzx eax,[g0]; mov [rsi+..],al; movzx eax,[paused]; mov [rsi+..],al; movzx eax,[g2]; mov [rsi+..],al
        new("state copy", Parse("0F B6 05 ?? ?? ?? ?? 88 86 ?? ?? 00 00 0F B6 05 ?? ?? ?? ?? 88 86 ?? ?? 00 00 0F B6 05 ?? ?? ?? ?? 88 86 ?? ?? 00 00"), 16, 20),
        // movzx eax,byte [rbp+..]; mov [paused],al; mov rcx,r14; call [rip+..]
        new("flag write", Parse("0F B6 45 ?? 88 05 ?? ?? ?? ?? 49 8B CE FF 15"), 6, 10),
    ];

    private long _moduleBase;
    private long _address;
    private Task<SearchResult> _search;
    private bool _reported;

    private sealed record SearchResult(long Address, string Details);

    /// <summary>Where the flag was found, or why it wasn't.</summary>
    public string Status { get; private set; } = "Not searched yet";

    /// <summary>True while the search for the current game process is running.</summary>
    public bool Searching => _search is { IsCompleted: false } || (_search != null && !_reported);

    /// <summary>True once the flag has been found for the current game process.</summary>
    public bool Found => _address != 0;

    /// <summary>
    /// Starts the search when there's a new game process, and reports the result once it finishes.
    /// Cheap to call every frame.
    /// </summary>
    public void Update(IMemory memory, Action<string> logMessage, Action<string> logError)
    {
        var moduleBase = memory.AddressOfProcess;
        if (moduleBase == 0)
        {
            return;
        }

        if (_search == null || moduleBase != _moduleBase)
        {
            _moduleBase = moduleBase;
            _address = 0;
            _reported = false;
            Status = "Searching";
            _search = Task.Run(() => Find(memory, moduleBase));
        }

        if (_reported || !_search.IsCompleted)
        {
            return;
        }

        _reported = true;
        _address = _search.IsCompletedSuccessfully ? _search.Result.Address : 0;
        if (_address != 0)
        {
            Status = $"Found at PathOfExile.exe+0x{_address - moduleBase:X}";
            logMessage($"ReAgent: game pause flag found at PathOfExile.exe+0x{_address - moduleBase:X}");
        }
        else
        {
            var details = _search.IsCompletedSuccessfully ? _search.Result.Details : _search.Exception?.GetBaseException().Message;
            Status = $"Not found ({details})";
            logError($"ReAgent couldn't find the game's pause flag ({details}), so it can't tell when the game is paused.");
        }
    }

    /// <summary>
    /// True when the game is paused. False while the search is still running, or if the flag
    /// couldn't be found, so a failed search never blocks actions.
    /// </summary>
    public bool IsPaused(IMemory memory, Action<string> logMessage, Action<string> logError)
    {
        Update(memory, logMessage, logError);
        return _address != 0 && memory.Read<byte>(_address) == 1;
    }

    private static SearchResult Find(IMemory memory, long moduleBase)
    {
        var header = memory.ReadBytes(moduleBase, 0x1000);
        if (header == null || header.Length < 0x1000)
        {
            return new SearchResult(0, "couldn't read the game's module header");
        }

        var text = FindSection(header, ".text");
        var data = FindSection(header, ".data");
        if (text.Size == 0 || data.Size == 0)
        {
            return new SearchResult(0, "couldn't find the game's code or data section");
        }

        var found = new long[Signatures.Length];
        const int chunkSize = 4 << 20;
        var overlap = Signatures.Max(s => s.Bytes.Length) - 1;
        for (long offset = 0; offset < text.Size && found.Any(a => a == 0); offset += chunkSize)
        {
            var size = (int)Math.Min(chunkSize + overlap, text.Size - offset);
            var chunkStart = moduleBase + text.Rva + offset;
            var chunk = memory.ReadBytes(chunkStart, size);
            if (chunk == null || chunk.Length < size)
            {
                continue;
            }

            for (var i = 0; i < Signatures.Length; i++)
            {
                if (found[i] != 0)
                {
                    continue;
                }

                var signature = Signatures[i];
                var at = IndexOf(chunk, signature.Bytes);
                if (at < 0)
                {
                    continue;
                }

                var displacement = BitConverter.ToInt32(chunk, at + signature.DisplacementOffset);
                found[i] = chunkStart + at + signature.InstructionEnd + displacement;
            }
        }

        // Only trust an address inside the game's data section that every matching signature agrees on.
        var dataStart = moduleBase + data.Rva;
        var addresses = found.Where(a => a >= dataStart && a < dataStart + data.Size).Distinct().ToList();
        if (addresses.Count == 1)
        {
            return new SearchResult(addresses[0], null);
        }

        var perSignature = string.Join(", ", Signatures.Select((s, i) => found[i] == 0 ? $"{s.Name}: no match" : $"{s.Name}: +0x{found[i] - moduleBase:X}"));
        return new SearchResult(0, addresses.Count == 0 ? perSignature : $"signatures disagree: {perSignature}");
    }

    private static (int Rva, int Size) FindSection(byte[] header, string name)
    {
        var peHeader = BitConverter.ToInt32(header, 0x3C);
        if (peHeader <= 0 || peHeader + 24 > header.Length)
        {
            return default;
        }

        int sectionCount = BitConverter.ToUInt16(header, peHeader + 6);
        int optionalHeaderSize = BitConverter.ToUInt16(header, peHeader + 20);
        for (var i = 0; i < sectionCount; i++)
        {
            var section = peHeader + 24 + optionalHeaderSize + i * 40;
            if (section + 40 > header.Length)
            {
                break;
            }

            if (Encoding.ASCII.GetString(header, section, 8).TrimEnd('\0') == name)
            {
                return (BitConverter.ToInt32(header, section + 12), BitConverter.ToInt32(header, section + 8));
            }
        }

        return default;
    }

    private static int IndexOf(byte[] haystack, byte?[] pattern)
    {
        var first = pattern[0]!.Value;
        var last = haystack.Length - pattern.Length;
        for (var i = Array.IndexOf(haystack, first); i >= 0 && i <= last; i = Array.IndexOf(haystack, first, i + 1))
        {
            var j = 1;
            while (j < pattern.Length && (pattern[j] == null || pattern[j] == haystack[i + j]))
            {
                j++;
            }

            if (j == pattern.Length)
            {
                return i;
            }
        }

        return -1;
    }

    private static byte?[] Parse(string pattern) =>
        pattern.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Select(b => b == "??" ? (byte?)null : Convert.ToByte(b, 16))
            .ToArray();
}
