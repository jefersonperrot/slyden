using Slyden.Core.Config;
using Slyden.Core.Render;

namespace Slyden.Tests;

public class TextToolsTests
{
    private static SlideshowConfig WithText(TextBlock text) => new()
    {
        Photos = [new PhotoSpec { File = "a.jpg", Texts = [text] }],
    };

    [Fact]
    public void Font_list_has_entries_in_alphabetical_order_without_duplicates()
    {
        var families = FontResolver.ListFamilies();
        Assert.NotEmpty(families);
        Assert.Equal(families.Count, families.Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.Equal(families.Order(StringComparer.CurrentCultureIgnoreCase), families);
    }

    [Fact]
    public void Centered_text_bounds_are_around_the_middle_of_the_frame()
    {
        var cfg = WithText(new TextBlock { Text = "Maria e João", Align = TextAlign.Center, X = 0.5, Y = 0.5, Size = 0.1 });
        var b = Assert.Single(SlidePreview.TextBounds(cfg, 0));
        Assert.True(b.Right > b.Left && b.Bottom > b.Top);
        Assert.Equal(0.5, (b.Left + b.Right) / 2, 2);
        Assert.Equal(0.5, (b.Top + b.Bottom) / 2, 2);
    }

    [Fact]
    public void Left_aligned_text_starts_at_x_and_right_aligned_ends_at_x()
    {
        var left = Assert.Single(SlidePreview.TextBounds(
            WithText(new TextBlock { Text = "Olá", Align = TextAlign.Left, X = 0.2, Y = 0.5 }), 0));
        var right = Assert.Single(SlidePreview.TextBounds(
            WithText(new TextBlock { Text = "Olá", Align = TextAlign.Right, X = 0.8, Y = 0.5 }), 0));
        Assert.Equal(0.2, left.Left, 3);
        Assert.Equal(0.8, right.Right, 3);
    }

    [Fact]
    public void Empty_text_has_empty_bounds()
    {
        var b = Assert.Single(SlidePreview.TextBounds(WithText(new TextBlock { Text = "  " }), 0));
        Assert.Equal(b.Left, b.Right);
    }
}
