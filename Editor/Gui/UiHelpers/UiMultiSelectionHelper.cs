#nullable enable
using System;
using System.Collections.Generic;
using ImGuiNET;

namespace T3.Editor.Gui.UiHelpers;

/// <summary>
/// A generic helper for managing multi-selection in UI lists.
/// Supports click, Ctrl+click (toggle), and Shift+click (range) selection.
/// </summary>
/// <typeparam name="TKey">The type of key used to identify items (e.g., string, Guid).</typeparam>
internal sealed class UiMultiSelectionHelper<TKey> where TKey : notnull
{
    private readonly HashSet<TKey> _selected = new();
    private int _anchorIndex = -1;
    private IReadOnlyList<TKey>? _lastVisibleItems;
    private TKey? _lastSelectedKey;

    /// <summary>
    /// Gets the set of currently selected keys.
    /// </summary>
    public IReadOnlySet<TKey> Selected => _selected;

    /// <summary>
    /// Gets the most recently selected/clicked item key.
    /// </summary>
    public TKey? LastSelectedKey => _lastSelectedKey;

    /// <summary>
    /// Gets the number of selected items.
    /// </summary>
    public int Count => _selected.Count;

    /// <summary>
    /// Checks if a specific key is selected.
    /// </summary>
    public bool IsSelected(TKey key) => _selected.Contains(key);

    /// <summary>
    /// Clears all selections and resets anchor.
    /// </summary>
    public void Clear()
    {
        _selected.Clear();
        _anchorIndex = -1;
    }

    /// <summary>
    /// Updates the visible items list. Call this each frame before processing clicks.
    /// </summary>
    /// <param name="visibleItems">The current list of visible/displayed items in order.</param>
    public void SetVisibleItems(IReadOnlyList<TKey> visibleItems)
    {
        _lastVisibleItems = visibleItems;
    }

    /// <summary>
    /// Handles a click on an item, applying Ctrl/Shift modifier logic.
    /// Call this when an item is clicked (e.g., after ImGui.Selectable returns true).
    /// </summary>
    /// <param name="key">The key of the clicked item.</param>
    /// <param name="index">The index of the clicked item in the visible items list.</param>
    public void HandleClick(TKey key, int index)
    {
        var io = ImGui.GetIO();
        bool ctrl = io.KeyCtrl;
        bool shift = io.KeyShift;

        // Track the last clicked item
        _lastSelectedKey = key;

        if (shift && _anchorIndex >= 0 && _lastVisibleItems != null)
        {
            // Range selection
            var min = Math.Min(_anchorIndex, index);
            var max = Math.Max(_anchorIndex, index);

            if (!ctrl)
                _selected.Clear();

            for (int i = min; i <= max; i++)
            {
                if (i >= 0 && i < _lastVisibleItems.Count)
                    _selected.Add(_lastVisibleItems[i]);
            }
        }
        else if (ctrl)
        {
            // Toggle selection
            if (_selected.Contains(key))
                _selected.Remove(key);
            else
                _selected.Add(key);

            _anchorIndex = index;
        }
        else
        {
            // Single selection (clear others)
            _selected.Clear();
            _selected.Add(key);
            _anchorIndex = index;
        }
    }


    #region --- Convenience Methods ---
    
    /// <summary>
    /// Selects a single item, clearing other selections.
    /// </summary>
    public void SelectSingle(TKey key, int index)
    {
        _selected.Clear();
        _selected.Add(key);
        _anchorIndex = index;
    }

    /// <summary>
    /// Adds a key to the selection without clearing others.
    /// </summary>
    public void AddToSelection(TKey key)
    {
        _selected.Add(key);
    }

    /// <summary>
    /// Removes a key from the selection.
    /// </summary>
    public void RemoveFromSelection(TKey key)
    {
        _selected.Remove(key);
    }

    /// <summary>
    /// Gets the selected items as a new HashSet (useful for passing to methods).
    /// </summary>
    public HashSet<TKey> GetSelectedSet() => new(_selected);
    
    #endregion

    /// <summary>
    /// Gets the selected items, or a fallback single item if nothing is selected.
    /// Useful for context menu actions where you want to act on selection or the right-clicked item.
    /// </summary>
    /// <param name="fallbackKey">The key to use if nothing is selected.</param>
    public IEnumerable<TKey> GetSelectedOrFallback(TKey fallbackKey)
    {
        return _selected.Count > 0 ? _selected : new[] { fallbackKey };
    }
}

