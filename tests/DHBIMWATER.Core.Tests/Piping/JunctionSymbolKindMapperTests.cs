using DHBIMWATER.Core.Piping;
using Xunit;

namespace DHBIMWATER.Core.Tests.Piping;

public sealed class JunctionSymbolKindMapperTests
{
    [Theory]
    [InlineData("직관", "단관", JunctionSymbolKind.Pipe)]
    [InlineData("단관", "단관", JunctionSymbolKind.Pipe)]
    [InlineData("T형", "T관", JunctionSymbolKind.Tee)]
    [InlineData("제수변", "제수변", JunctionSymbolKind.Gate)]
    [InlineData("공기변", "공기변", JunctionSymbolKind.Air)]
    [InlineData("미확인 패밀리", "이름", JunctionSymbolKind.Generic)]
    public void Map_UsesKnownVocabularyThenFallsBack(string part, string name, JunctionSymbolKind expected)
        => Assert.Equal(expected, JunctionSymbolKindMapper.Map(part, name));

    [Fact]
    public void Map_UsesItemNameWhenPartIsEmpty()
        => Assert.Equal(JunctionSymbolKind.Gate, JunctionSymbolKindMapper.Map(" ", "제수변"));
}
