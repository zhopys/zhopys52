using MiniFinance.Data.Models;
using MiniFinance.Services;
using Xunit;

namespace MiniFinance.Tests;

public class CounterpartyNameMatcherTests
{
    [Fact]
    public void FindBestMatch_treats_similar_names_as_same()
    {
        var existing = new List<CounterpartyRecord>
        {
            new() { Id = 1, Name = "MALINOVKA/ SHOP", UserId = "u" }
        };

        var match = CounterpartyNameMatcher.FindBestMatch("MALINOVKA SHOP MINSK", existing);
        Assert.NotNull(match);
        Assert.Equal(1, match!.Id);
    }

    [Fact]
    public void NormalizeKey_ignores_city_noise()
    {
        var a = CounterpartyNameMatcher.NormalizeKey("MALINOVKA/ SHOP");
        var b = CounterpartyNameMatcher.NormalizeKey("MALINOVKA SHOP MINSK");
        Assert.Equal(a, b);
    }
}
