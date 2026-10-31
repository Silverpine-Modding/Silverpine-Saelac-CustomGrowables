#nullable enable
using System;
using System.Collections.Generic;
using UnityEngine;

namespace SilverpineMods.CustomGrowables;

internal sealed partial class GrowableEditorUI
{
    private GUIStyle? itemTitleStyle, itemIdStyle, itemCardStyle, tooltipStyle;
    private readonly Dictionary<(GUIStyle Style, string Text), (float Width, string Text)> fittedText = new();

    private void EnsureTextStyles()
    {
        itemTitleStyle = new GUIStyle(editorFieldLabel!)
        {
            alignment = TextAnchor.MiddleLeft, padding = new RectOffset(),
            clipping = TextClipping.Clip, richText = false
        };
        itemIdStyle = new GUIStyle(itemTitleStyle) { fontSize = 18 };
        itemIdStyle.normal.textColor = new Color(.65f, .69f, .68f);
        itemCardStyle = new GUIStyle(editorAction!) { padding = new RectOffset(), fixedHeight = 0f };
        tooltipStyle = new GUIStyle(editorSkin!.box)
        {
            fontSize = 18, alignment = TextAnchor.UpperLeft,
            padding = new RectOffset(14, 14, 12, 12), wordWrap = true, richText = false
        };
        fittedText.Clear();
    }

    private string FitEditorText(string text, float width, GUIStyle style)
    {
        // GetRect returns a placeholder during Layout. Do not evict the real
        // width's fitted string every time Layout precedes Repaint.
        if (Event.current.type == EventType.Layout) return "";
        width = Mathf.Max(0, Mathf.Floor(width));
        var key = (style, text);
        if (fittedText.TryGetValue(key, out var cached) && cached.Width == width) return cached.Text;
        string fitted = EditorTextLayout.FitSingleLine(text, width, value => style.CalcSize(new GUIContent(value)).x);
        if (fittedText.Count >= 1024) fittedText.Clear();
        fittedText[key] = (width, fitted);
        return fitted;
    }

    private bool DrawItemCard(string title, string id, string tooltip)
    {
        // Allocate the card independently of its text. Neither name nor ID can
        // expand into a third line or overlap its neighbor inside a scroll view.
        Rect row = GUILayoutUtility.GetRect(0f, 10000f, EditorTextLayout.ItemRowHeight,
            EditorTextLayout.ItemRowHeight, GUILayout.ExpandWidth(true));
        bool clicked = GUI.Button(row, new GUIContent("", tooltip), itemCardStyle!);
        Rect nameRect = new(row.x + 14, row.y + 9, Mathf.Max(0, row.width - 28), 28);
        Rect idRect = new(row.x + 14, row.y + 40, Mathf.Max(0, row.width - 28), 24);
        GUI.Label(nameRect, new GUIContent(FitEditorText(title, nameRect.width, itemTitleStyle!), tooltip), itemTitleStyle!);
        GUI.Label(idRect, new GUIContent(FitEditorText(id, idRect.width, itemIdStyle!), tooltip), itemIdStyle!);
        GUILayout.Space(6);
        return clicked;
    }

    private bool DrawSingleLineChoice(string text)
    {
        Rect row = GUILayoutUtility.GetRect(0f, 10000f, 46f, 46f, GUILayout.ExpandWidth(true));
        return GUI.Button(row, new GUIContent(FitEditorText(text, row.width - 8f, editorAction!), text), editorAction!);
    }

    private void DrawSingleLineLabel(string text)
    {
        Rect row = GUILayoutUtility.GetRect(0f, 10000f, 44f, 44f, GUILayout.ExpandWidth(true));
        GUI.Label(row, new GUIContent(FitEditorText(text, row.width - 8f, itemTitleStyle!), text), itemTitleStyle!);
    }

    private void DrawEditorTooltip()
    {
        // IMGUI does not draw GUIContent.tooltips by itself.
        if (Event.current.type != EventType.Repaint || string.IsNullOrEmpty(GUI.tooltip)) return;
        var content = new GUIContent(GUI.tooltip);
        float width = Mathf.Clamp(tooltipStyle!.CalcSize(content).x, 200f, 720f);
        float height = Mathf.Min(DesignHeight - 32f, tooltipStyle.CalcHeight(content, width));
        Vector2 mouse = Event.current.mousePosition;
        var rect = new Rect(Mathf.Clamp(mouse.x + 18, 16, DesignWidth - width - 16),
            Mathf.Clamp(mouse.y + 24, 16, DesignHeight - height - 16), width, height);
        GUI.Label(rect, content, tooltipStyle);
    }
}
