using DHBIMWATER.Core.Families;
using Xunit;

namespace DHBIMWATER.Core.Tests.Families;

/// <summary>docs/309 § 6 Q5 — 이미지 경로는 이미지 폴더 기준 상대경로, 폴더 밖은 절대경로.</summary>
public sealed class TypeImagePathsTests
{
    private const string Folder = @"C:\img";

    [Fact]
    public void ToStored_InsideFolder_IsRelative()
        => Assert.Equal(@"valve\gv.png", TypeImagePaths.ToStored(Folder, @"C:\img\valve\gv.png"));

    [Theory]
    [InlineData(@"C:\other\gv.png")]
    [InlineData(@"D:\img\gv.png")]
    public void ToStored_OutsideFolder_IsAbsolute(string path)
        => Assert.Equal(path, TypeImagePaths.ToStored(Folder, path));

    [Fact]
    public void ToStored_NoFolder_IsAbsolute()
        => Assert.Equal(@"C:\img\gv.png", TypeImagePaths.ToStored("", @"C:\img\gv.png"));

    [Fact]
    public void Resolve_Relative_UsesFolder()
        => Assert.Equal(@"C:\img\valve\gv.png", TypeImagePaths.Resolve(Folder, @"valve\gv.png"));

    [Fact]
    public void Resolve_Absolute_IgnoresFolder()
        => Assert.Equal(@"D:\x\gv.png", TypeImagePaths.Resolve(Folder, @"D:\x\gv.png"));

    [Theory]
    [InlineData("", @"gv.png")]
    [InlineData(Folder, "")]
    [InlineData(Folder, null)]
    public void Resolve_Unresolvable_IsNull(string folder, string? stored)
        => Assert.Null(TypeImagePaths.Resolve(folder, stored));

    [Fact]
    public void RoundTrip_KeepsPath()
    {
        var full = @"C:\img\a\b.jpg";
        Assert.Equal(full, TypeImagePaths.Resolve(Folder, TypeImagePaths.ToStored(Folder, full)));
    }

    [Fact]
    public void FolderKey_IgnoresCaseAndTrailingSeparator()
        => Assert.Equal(TypeImagePaths.FolderKey(@"C:\Fam\"), TypeImagePaths.FolderKey(@"c:\fam"));

    [Theory]
    [InlineData("a.PNG", true)]
    [InlineData("a.jpeg", true)]
    [InlineData("a.gif", false)]
    [InlineData("a.rfa", false)]
    public void IsImageFile(string path, bool expected)
        => Assert.Equal(expected, TypeImagePaths.IsImageFile(path));
}
