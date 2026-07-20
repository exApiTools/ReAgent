using System;
using System.Numerics;
using System.Runtime.InteropServices;
using ImGuiNET;
using ReAgent.State;

namespace ReAgent.Autocomplete;

/// <summary>
/// Drop-in replacement for the plain rule-source InputTextMultiline that adds an as-you-type
/// completion popup. Only one editor is active at a time (ImGui guarantees one active InputText),
/// so all state is static and keyed to the active widget id.
///
/// Keyboard handling happens inside the InputText callback: it runs after ImGui applied this
/// frame's keys, which lets us undo the caret line-jump from Up/Down and remove the newline that
/// Enter inserted before treating them as popup navigation/acceptance.
/// </summary>
internal static class RuleSourceEditor
{
    public static bool Enabled = true;

    private const int MaxVisibleItems = 12;

    private static readonly unsafe ImGuiInputTextCallback Callback = CallbackImpl;

    private static uint _activeId;
    private static string _text = "";
    private static int _caret;
    private static CompletionResult _result = CompletionResult.Empty;
    private static int _selected;
    private static bool _popupVisible;
    private static bool _selectionMoved;
    private static bool _suppressUntilEdit;
    private static bool _forceOpen;
    private static bool _wantRefocus;
    private static int _pendingCaret = -1;
    private static string _mouseInsert;
    private static bool _textDirty = true;
    private static int _syntaxVersion = 1;

    public static bool Draw(string label, ref string source, RuleState state, int syntaxVersion, RuleActionType actionType)
    {
        var size = new Vector2(
            ImGui.GetContentRegionAvail().X,
            ImGui.CalcTextSize($"^{source}_").Y + ImGui.GetTextLineHeight());

        if (!Enabled)
        {
            return ImGui.InputTextMultiline(label, ref source, 10000, size);
        }

        if (_wantRefocus)
        {
            ImGui.SetKeyboardFocusHere();
            _wantRefocus = false;
        }

        var changed = ImGui.InputTextMultiline(label, ref source, 10000, size,
            ImGuiInputTextFlags.CallbackAlways | ImGuiInputTextFlags.CallbackCompletion, Callback);

        var id = ImGui.GetItemID();
        var active = ImGui.IsItemActive();
        if (!active)
        {
            if (id == _activeId)
            {
                ResetSession();
            }

            return changed;
        }

        if (id != _activeId)
        {
            ResetSession();
            _activeId = id;
            _text = source;
            _caret = source.Length;
        }

        // Mouse-click acceptance happens outside the callback: the click deactivated the input, so
        // we splice the string directly and refocus with a pending caret position.
        if (_mouseInsert != null)
        {
            var replaceStart = Math.Clamp(_result.ReplaceStart, 0, source.Length);
            var caret = Math.Clamp(_caret, replaceStart, source.Length);
            source = source[..replaceStart] + _mouseInsert + source[caret..];
            _pendingCaret = replaceStart + _mouseInsert.Length;
            _text = source;
            _mouseInsert = null;
            _wantRefocus = true;
            _suppressUntilEdit = true;
            _popupVisible = false;
            return true;
        }

        if (ImGui.IsKeyPressed(ImGuiKey.Space) && ImGui.GetIO().KeyCtrl)
        {
            _forceOpen = true;
            _suppressUntilEdit = false;
            _textDirty = true;
        }

        if (_textDirty)
        {
            _result = CompletionEngine.GetCompletions(_text, _caret, syntaxVersion, actionType, state);
            _selected = 0;
            _textDirty = false;
        }

        _syntaxVersion = syntaxVersion;

        _popupVisible = !_suppressUntilEdit &&
                        _result.Items.Count > 0 &&
                        (_result.AutoShow || _forceOpen);
        if (_popupVisible)
        {
            DrawPopup();
        }

        return changed;
    }

    private static void ResetSession()
    {
        _activeId = 0;
        _result = CompletionResult.Empty;
        _selected = 0;
        _popupVisible = false;
        _suppressUntilEdit = false;
        _forceOpen = false;
        _mouseInsert = null;
        _textDirty = true;
    }

    private static void DrawPopup()
    {
        var pos = CaretScreenPosition();
        var lineHeight = ImGui.GetTextLineHeightWithSpacing();
        var estHeight = Math.Min(_result.Items.Count, MaxVisibleItems) * lineHeight + lineHeight + 16;
        var display = ImGui.GetIO().DisplaySize;
        if (pos.Y + estHeight > display.Y)
        {
            pos.Y = Math.Max(0, pos.Y - estHeight - ImGui.GetTextLineHeight() * 1.5f);
        }

        pos.X = Math.Min(pos.X, Math.Max(0, display.X - 340));

        ImGui.SetNextWindowPos(pos);
        ImGui.SetNextWindowSizeConstraints(new Vector2(260, 0), new Vector2(620, MaxVisibleItems * lineHeight + 24));
        const ImGuiWindowFlags flags = ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoMove |
                                       ImGuiWindowFlags.NoSavedSettings | ImGuiWindowFlags.AlwaysAutoResize |
                                       ImGuiWindowFlags.NoFocusOnAppearing | ImGuiWindowFlags.NoNav | ImGuiWindowFlags.Tooltip;
        if (ImGui.Begin("##ReAgentAutocomplete", flags))
        {
            for (var i = 0; i < _result.Items.Count; i++)
            {
                var item = _result.Items[i];
                if (ImGui.Selectable($"{item.Label}##ac{i}", i == _selected))
                {
                    _selected = i;
                    _mouseInsert = item.InsertText;
                }

                if (_selectionMoved && i == _selected)
                {
                    ImGui.SetScrollHereY(0.5f);
                }

                if (!string.IsNullOrEmpty(item.Detail))
                {
                    ImGui.SameLine();
                    ImGui.TextDisabled(item.Detail);
                }
            }

            _selectionMoved = false;
            ImGui.Separator();
            ImGui.TextDisabled(_syntaxVersion == 2
                ? "Tab/Enter insert · Up/Down select · new syntax starts from State."
                : "Tab/Enter insert · Up/Down select · Ctrl+Space open");
        }

        ImGui.End();
    }

    private static Vector2 CaretScreenPosition()
    {
        var rectMin = ImGui.GetItemRectMin();
        var padding = ImGui.GetStyle().FramePadding;
        var caret = Math.Clamp(_caret, 0, _text.Length);
        var span = _text.AsSpan(0, caret);
        var lineStart = span.LastIndexOf('\n') + 1;
        var lineIndex = 0;
        foreach (var c in span)
        {
            if (c == '\n')
            {
                lineIndex++;
            }
        }

        var x = rectMin.X + padding.X + ImGui.CalcTextSize(_text[lineStart..caret]).X;
        var y = rectMin.Y + padding.Y + (lineIndex + 1) * ImGui.GetTextLineHeight() + 3;
        return new Vector2(x, y);
    }

    private static unsafe int CallbackImpl(ImGuiInputTextCallbackData* dataPtr)
    {
        try
        {
            var data = new ImGuiInputTextCallbackDataPtr(dataPtr);
            HandleCallback(data);
        }
        catch
        {
            // The callback runs inside ImGui's text processing — never let it throw.
        }

        return 0;
    }

    private static void HandleCallback(ImGuiInputTextCallbackDataPtr data)
    {
        if (data.EventFlag == ImGuiInputTextFlags.CallbackCompletion)
        {
            if (_popupVisible && _selected < _result.Items.Count)
            {
                ApplyInsert(data);
            }

            return;
        }

        var caretBefore = _caret;

        if (_popupVisible && _result.Items.Count > 0)
        {
            if (ImGui.IsKeyPressed(ImGuiKey.DownArrow))
            {
                _selected = (_selected + 1) % _result.Items.Count;
                _selectionMoved = true;
                RestoreCaret(data, caretBefore);
            }
            else if (ImGui.IsKeyPressed(ImGuiKey.UpArrow))
            {
                _selected = (_selected - 1 + _result.Items.Count) % _result.Items.Count;
                _selectionMoved = true;
                RestoreCaret(data, caretBefore);
            }
            else if ((ImGui.IsKeyPressed(ImGuiKey.Enter, false) || ImGui.IsKeyPressed(ImGuiKey.KeypadEnter, false)) &&
                     _selected < _result.Items.Count)
            {
                // ImGui already inserted the newline before this callback ran; remove it, then insert.
                if (data.CursorPos > 0 && data.Buf != IntPtr.Zero &&
                    Marshal.ReadByte(data.Buf, data.CursorPos - 1) == (byte)'\n')
                {
                    data.DeleteChars(data.CursorPos - 1, 1);
                }

                ApplyInsert(data);
            }
            else if (ImGui.IsKeyPressed(ImGuiKey.Escape, false))
            {
                _suppressUntilEdit = true;
            }
        }

        if (_pendingCaret >= 0)
        {
            data.CursorPos = Math.Clamp(_pendingCaret, 0, data.BufTextLen);
            data.SelectionStart = data.CursorPos;
            data.SelectionEnd = data.CursorPos;
            _pendingCaret = -1;
        }

        var newText = ReadBuffer(data);
        if (!string.Equals(newText, _text, StringComparison.Ordinal))
        {
            _text = newText;
            _suppressUntilEdit = false;
            _forceOpen = false;
            _textDirty = true;
        }

        if (data.CursorPos != _caret)
        {
            _caret = data.CursorPos;
            _textDirty = true;
        }
    }

    private static void RestoreCaret(ImGuiInputTextCallbackDataPtr data, int caret)
    {
        data.CursorPos = Math.Clamp(caret, 0, data.BufTextLen);
        data.SelectionStart = data.CursorPos;
        data.SelectionEnd = data.CursorPos;
    }

    private static void ApplyInsert(ImGuiInputTextCallbackDataPtr data)
    {
        var item = _result.Items[_selected];
        var replaceStart = Math.Clamp(_result.ReplaceStart, 0, data.BufTextLen);
        var cursor = Math.Clamp(data.CursorPos, replaceStart, data.BufTextLen);
        data.DeleteChars(replaceStart, cursor - replaceStart);
        data.InsertChars(replaceStart, item.InsertText);
        data.CursorPos = replaceStart + item.InsertText.Length;
        _suppressUntilEdit = true;
        _popupVisible = false;
    }

    private static string ReadBuffer(ImGuiInputTextCallbackDataPtr data)
    {
        return data.Buf == IntPtr.Zero || data.BufTextLen <= 0
            ? ""
            : Marshal.PtrToStringUTF8(data.Buf, data.BufTextLen) ?? "";
    }
}
