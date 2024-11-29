using System;
using System.Collections.Generic;
using System.Numerics;
using ImGuiNET;

namespace ReAgent;

public static class ImguiExt
{
    public static bool? DrawDeleteConfirmationPopup(string id, string deletedItemName)
    {
        bool? rv = null;
        if (ImGui.BeginPopup(id))
        {
            ImGui.TextUnformatted($"Really delete {deletedItemName}?");
            if (ImGui.Button("Yes"))
            {
                ImGui.CloseCurrentPopup();
                rv = true;
            }

            ImGui.SameLine();
            if (ImGui.Button("No"))
            {
                ImGui.CloseCurrentPopup();
                rv = false;
            }

            ImGui.EndPopup();
        }
        else
        {
            return false;
        }

        return rv;
    }

    public static unsafe bool SetDragDropPayload<T>(string id, T payload) where T : unmanaged
    {
        return ImGui.SetDragDropPayload(id, (IntPtr)(&payload), (uint)sizeof(T));
    }

    public static unsafe T? AcceptDragDropPayload<T>(string id) where T : unmanaged
    {
        var ptr = ImGui.AcceptDragDropPayload(id);
        if (ptr.NativePtr != null)
        {
            var data = *(T*)ptr.Data;
            return data;
        }

        return null;
    }

    public static void DrawLargeTransparentSelectable(string id, Vector2 start)
    {
        var cursor = ImGui.GetCursorPos();
        ImGui.SetCursorPos(start);
        ImGui.PushStyleColor(ImGuiCol.HeaderHovered, 0);
        ImGui.PushStyleColor(ImGuiCol.HeaderActive, 0);
        ImGui.Selectable(id, false, ImGuiSelectableFlags.SpanAllColumns, new Vector2(0, cursor.Y - start.Y));
        ImGui.PopStyleColor(2);
        ImGui.SetCursorPos(cursor);
    }

    public static bool EnumerableComboBox<T>(string displayText, IEnumerable<T> items, ref T current)
    {
        var picked = false;
        if (ImGui.BeginCombo(displayText, $"{current}"))
        {
            var counter = 0;
            foreach (var item in items)
            {
                var selected = item.Equals(current);
                if (ImGui.IsWindowAppearing() && selected)
                {
                    ImGui.SetScrollHereY();
                }

                if (ImGui.Selectable($"{item}###{counter}", selected))
                {
                    current = item;
                    picked = true;
                    break;
                }

                counter++;
            }

            ImGui.EndCombo();
        }

        return picked;
    }
}