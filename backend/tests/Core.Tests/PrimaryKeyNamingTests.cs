using Saaskit.Core.Data;

namespace Saaskit.Core.Tests;

public sealed class PrimaryKeyNamingTests
{
    [Theory]
    [InlineData("Principals", "PrincipalId")]
    [InlineData("AuditLog", "AuditLogId")]
    [InlineData("Categories", "CategoryId")]
    [InlineData("ModuleAccess", "ModuleAccessId")]
    [InlineData("MsFarm", "MsFarmId")]
    [InlineData("MsStatus", "MsStatusId")]
    [InlineData("MsFarmBlock", "MsFarmBlockId")]
    [InlineData("MsCategory", "MsCategoryId")]
    public void A_key_is_named_after_the_singular_table(string table, string expected) =>
        PlatformDbContext.PrimaryKeyColumnName(table).ShouldBe(expected);

    [Theory]
    [InlineData("Msgs", "MsgId")]
    public void Only_Ms_followed_by_an_uppercase_letter_is_a_master_table(string table, string expected) =>
        PlatformDbContext.PrimaryKeyColumnName(table).ShouldBe(expected);
}
