namespace Slyden.Core.Config;

public static class ListReorder
{
    /// <summary>
    /// Leva os itens nas posições <paramref name="indices"/> para antes da posição <paramref name="insertBefore"/>
    /// (0 = início, Count = fim), mantendo a ordem relativa entre eles. Devolve as novas posições dos itens movidos.
    /// </summary>
    public static IReadOnlyList<int> MoveBlock<T>(IList<T> list, IEnumerable<int> indices, int insertBefore)
    {
        var chosen = indices.Distinct().Where(i => i >= 0 && i < list.Count).Order().ToList();
        if (chosen.Count == 0) return [];

        insertBefore = Math.Clamp(insertBefore, 0, list.Count);
        var moved = chosen.Select(i => list[i]).ToList();
        int target = insertBefore - chosen.Count(i => i < insertBefore);

        for (int k = chosen.Count - 1; k >= 0; k--) list.RemoveAt(chosen[k]);
        for (int k = 0; k < moved.Count; k++) list.Insert(target + k, moved[k]);
        return Enumerable.Range(target, moved.Count).ToList();
    }
}
