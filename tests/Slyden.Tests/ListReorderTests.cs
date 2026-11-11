using Slyden.Core.Config;

namespace Slyden.Tests;

public class ListReorderTests
{
    private static List<char> Abc() => ['a', 'b', 'c', 'd', 'e'];

    [Fact]
    public void Moves_one_item_forward()
    {
        var list = Abc();
        var now = ListReorder.MoveBlock(list, [0], 3); // a antes de d
        Assert.Equal("bcade", new string(list.ToArray()));
        Assert.Equal([2], now);
    }

    [Fact]
    public void Moves_one_item_backward()
    {
        var list = Abc();
        var now = ListReorder.MoveBlock(list, [3], 1); // d antes de b
        Assert.Equal("adbce", new string(list.ToArray()));
        Assert.Equal([1], now);
    }

    [Fact]
    public void Moves_to_the_start_and_to_the_end()
    {
        var list = Abc();
        ListReorder.MoveBlock(list, [4], 0);
        Assert.Equal("eabcd", new string(list.ToArray()));
        ListReorder.MoveBlock(list, [0], list.Count);
        Assert.Equal("abcde", new string(list.ToArray()));
    }

    [Fact]
    public void Moves_a_non_contiguous_block_together_keeping_relative_order()
    {
        var list = Abc();
        var now = ListReorder.MoveBlock(list, [3, 1], 5); // b e d para o fim
        Assert.Equal("acebd", new string(list.ToArray()));
        Assert.Equal([3, 4], now);
    }

    [Fact]
    public void Dropping_inside_the_selection_keeps_the_order()
    {
        var list = Abc();
        ListReorder.MoveBlock(list, [1, 2], 2);
        Assert.Equal("abcde", new string(list.ToArray()));
    }

    [Fact]
    public void Block_moved_before_an_earlier_position()
    {
        var list = Abc();
        var now = ListReorder.MoveBlock(list, [2, 3], 0);
        Assert.Equal("cdabe", new string(list.ToArray()));
        Assert.Equal([0, 1], now);
    }

    [Fact]
    public void Invalid_indices_and_out_of_range_target_are_handled()
    {
        var list = Abc();
        Assert.Empty(ListReorder.MoveBlock(list, [9, -1], 2));
        Assert.Equal("abcde", new string(list.ToArray()));
        ListReorder.MoveBlock(list, [0], 99);
        Assert.Equal("bcdea", new string(list.ToArray()));
    }

    [Fact]
    public void Works_with_observable_collections()
    {
        var list = new System.Collections.ObjectModel.ObservableCollection<int>([1, 2, 3, 4]);
        ListReorder.MoveBlock(list, [0], 3);
        Assert.Equal([2, 3, 1, 4], list.ToArray());
    }
}
