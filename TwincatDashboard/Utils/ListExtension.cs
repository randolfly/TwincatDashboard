﻿namespace TwincatDashboard.Utils;
public static class ListExtension
{
    public static void AddSorted<T>(this IList<T> list, T item, IComparer<T> comparer = null!) {
        comparer ??= Comparer<T>.Default;
        var i = 0;
        while (i < list.Count && comparer.Compare(list[i], item) < 0) i++;
        list.Insert(i, item);
    }
}