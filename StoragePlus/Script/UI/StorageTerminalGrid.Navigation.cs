using Pug.UnityExtensions;
using UnityEngine;

public sealed partial class StorageTerminalGrid
{
    private float MaxScroll => Mathf.Max(0f, GetCurrentWindowHeight() -
        (scrollWindow != null ? scrollWindow.windowHeight : VisibleRowCount * spread));

    public override UIelement GetClosestUIElement(Vector3 position)
    {
        UIelement closest = null;
        float closestDistance = float.MaxValue;
        if (itemSlots == null)
        {
            return null;
        }

        foreach (SlotUIBase slot in itemSlots)
        {
            if (slot == null || !slot.isShowing || !slot.isVisibleOnScreen)
            {
                continue;
            }

            float distance = (slot.transform.position - position).sqrMagnitude;
            if (distance < closestDistance)
            {
                closest = slot;
                closestDistance = distance;
            }
        }

        return closest;
    }

    internal UIelement GetAdjacentEntry(int index, Direction.Id direction, Vector3 position)
    {
        int nextIndex = GetAdjacentEntryIndex(index, _entries.Count, ColumnCount, direction);
        return nextIndex >= 0 ? RevealEntry(nextIndex) : base.GetAdjacentUIElement(direction, position);
    }

    internal static int GetAdjacentEntryIndex(int index, int count, int columns, Direction.Id direction)
    {
        if (index < 0 || index >= count || columns <= 0)
        {
            return -1;
        }

        int column = index % columns;
        int nextRow = (index / columns + 1) * columns;
        return direction switch
        {
            Direction.Id.forward when index >= columns => index - columns,
            Direction.Id.back when nextRow < count => Mathf.Min(index + columns, count - 1),
            Direction.Id.left when column > 0 => index - 1,
            Direction.Id.right when column < columns - 1 && index + 1 < count => index + 1,
            _ => -1
        };
    }

    internal StorageTerminalItemSlot RevealEntry(int index)
    {
        if (index < 0 || index >= _entries.Count || itemSlots == null)
        {
            return null;
        }

        int row = index / ColumnCount;
        int firstVisibleRow = Mathf.FloorToInt(_currentScroll / spread);
        int targetFirstRow = Mathf.Clamp(firstVisibleRow, row - VisibleRowCount + 1, row);
        float scroll = Mathf.Clamp(targetFirstRow * spread, 0f, MaxScroll);
        _currentScroll = scroll;
        Vector3 localPosition = itemSlotsRoot.transform.localPosition;
        localPosition.y = scroll;
        itemSlotsRoot.transform.localPosition = localPosition;
        UpdateVisibleSlots();
        RefreshScrollPresentation();

        int poolIndex = index - Mathf.FloorToInt(scroll / spread) * ColumnCount;
        return poolIndex >= 0 && poolIndex < itemSlots.Count
            ? itemSlots[poolIndex] as StorageTerminalItemSlot
            : null;
    }

    private void RestoreControllerSelection(StorageTerminalItemSlot.SelectionIdentity identity, int previousIndex)
    {
        int targetIndex = Mathf.Min(previousIndex, _entries.Count - 1);
        for (int i = 0; i < _entries.Count; i++)
        {
            StorageTerminalItemEntry entry = _entries[i];
            var candidate = new StorageTerminalItemSlot.SelectionIdentity(entry.ObjectId, entry.Variation, entry.EntryId, entry.Flags);
            if (candidate.Equals(identity))
            {
                targetIndex = i;
                break;
            }
        }

        UIelement target = RevealEntry(targetIndex);
        if (target == null)
        {
            target = _owner?.EmptyGridFocusTarget;
        }

        StorageTerminalUIUtility.SelectForController(target);
    }
}
