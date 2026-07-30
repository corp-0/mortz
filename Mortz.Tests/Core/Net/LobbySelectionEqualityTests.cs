using Mortz.Core.Net;
using Xunit;

namespace Mortz.Tests.Core.Net;

public class LobbySelectionEqualityTests
{
    private static LobbySelection Build() => new(
        "castlewars",
        "hash",
        new LobbyCatalog([new ContentOption("castlewars", "Castle Wars")]),
        new LobbyCatalog([new ContentOption("deathmatch", "Deathmatch")]),
        "deathmatch");

    [Fact]
    public void EqualButDistinctCatalogsCompareEqual()
    {
        LobbySelection a = Build();
        LobbySelection b = Build();

        Assert.Equal(a, b);
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
    }

    [Fact]
    public void ADifferingOptionComparesUnequal()
    {
        LobbySelection a = Build();
        LobbySelection b = a with
        {
            Maps = new LobbyCatalog([new ContentOption("otherworld", "Other World")]),
        };

        Assert.NotEqual(a, b);
    }

    [Fact]
    public void TheEmptyCatalogEqualsAFreshlyBuiltEmptyOne()
    {
        LobbyCatalog built = new([]);

        Assert.Equal(LobbyCatalog.EMPTY, built);
        Assert.Equal(LobbyCatalog.EMPTY.GetHashCode(), built.GetHashCode());
    }
}
