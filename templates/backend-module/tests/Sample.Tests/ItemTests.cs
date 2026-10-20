using Farm360.Sample.Domain;

namespace Farm360.Sample.Tests;

[Trait("service", "sample-key")]
public sealed class ItemTests
{
    [Fact]
    public void A_name_is_trimmed()
    {
        var item = new Item("  Widget  ");
        item.Name.ShouldBe("Widget");
        item.Rename("  Gadget ");
        item.Name.ShouldBe("Gadget");
    }

    [Fact]
    public void Deleting_marks_the_item_instead_of_removing_it()
    {
        var item = new Item("Widget");
        var when = new DateTime(2026, 10, 9, 1, 0, 0, DateTimeKind.Utc);
        item.MarkDeleted(when);
        item.IsDeleted.ShouldBeTrue();
        item.DeletedOn.ShouldBe(when);
    }
}
