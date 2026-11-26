using System.Runtime.CompilerServices;
using Microsoft.UI.Xaml.Controls;

namespace Qolome_Devtoy_App.Helpers;

/// <summary>
/// 多选 ListView：仅允许复选框/全选等编程方式改 SelectedItems；行点击选中会被回滚。
/// </summary>
public static class ListViewCheckOnlySelection
{
    private static readonly ConditionalWeakTable<ListViewBase, State> States = new();

    private sealed class State
    {
        public int ProgrammaticDepth;
        public bool SelectionHooked;
    }

    public static void Attach(ListViewBase list)
    {
        var state = States.GetOrCreateValue(list);
        if (state.SelectionHooked)
        {
            return;
        }

        state.SelectionHooked = true;
        list.SelectionChanged += OnSelectionChanged;
    }

    /// <summary>在复选框 / 全选里修改 SelectedItems 时包裹，避免被回滚。</summary>
    public static void RunProgrammatic(ListViewBase list, Action action)
    {
        Attach(list);
        var state = States.GetOrCreateValue(list);
        state.ProgrammaticDepth++;
        try
        {
            action();
        }
        finally
        {
            state.ProgrammaticDepth--;
        }
    }

    public static bool IsProgrammatic(ListViewBase list) =>
        States.TryGetValue(list, out var state) && state.ProgrammaticDepth > 0;

    private static void OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (sender is not ListViewBase list)
        {
            return;
        }

        var state = States.GetOrCreateValue(list);
        if (state.ProgrammaticDepth > 0)
        {
            return;
        }

        if (e.AddedItems.Count == 0 && e.RemovedItems.Count == 0)
        {
            return;
        }

        state.ProgrammaticDepth++;
        try
        {
            foreach (var item in e.AddedItems)
            {
                list.SelectedItems.Remove(item);
            }

            foreach (var item in e.RemovedItems)
            {
                list.SelectedItems.Add(item);
            }
        }
        finally
        {
            state.ProgrammaticDepth--;
        }
    }
}
