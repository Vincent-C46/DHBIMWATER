namespace DHBIMWATER.Core.Families;

/// <summary>
/// 패밀리 폴더 하나의 유형 이미지 매핑(docs/309).
/// <para>패밀리 키 = 패밀리 폴더 기준 파일명. 이미지 = 이미지 폴더 기준 상대경로, 폴더 밖이면 절대경로(§ 6 Q5).</para>
/// </summary>
public sealed record TypeImageMapping(string ImageFolder, IReadOnlyDictionary<string, string> Images)
{
    public static TypeImageMapping Empty { get; } = new("", new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));
}

/// <summary>매핑 경로 규칙. 순수 문자열 처리 — 파일 존재는 안 봄.</summary>
public static class TypeImagePaths
{
    /// <summary>유형 이미지로 넣을 수 있는 확장자. Revit ImageType이 읽는 래스터 형식만.</summary>
    public static readonly IReadOnlyList<string> Extensions = [".png", ".jpg", ".jpeg", ".bmp"];

    public static bool IsImageFile(string path)
        => Extensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase);

    /// <summary>이미지 폴더 안이면 상대경로, 밖이거나 폴더 미지정이면 절대경로 그대로.</summary>
    public static string ToStored(string imageFolder, string fullPath)
    {
        if (string.IsNullOrWhiteSpace(imageFolder)) return Path.GetFullPath(fullPath);
        var relative = Path.GetRelativePath(Path.GetFullPath(imageFolder), Path.GetFullPath(fullPath));
        // 폴더 밖(../) 또는 다른 드라이브(절대경로 반환)는 절대경로로 저장.
        return relative.StartsWith("..", StringComparison.Ordinal) || Path.IsPathRooted(relative)
            ? Path.GetFullPath(fullPath)
            : relative;
    }

    /// <summary>저장값 → 절대경로. 상대경로는 이미지 폴더 기준. 폴더 미지정 상대경로는 해석 불가 → null.</summary>
    public static string? Resolve(string imageFolder, string? stored)
    {
        if (string.IsNullOrWhiteSpace(stored)) return null;
        if (Path.IsPathRooted(stored)) return Path.GetFullPath(stored);
        if (string.IsNullOrWhiteSpace(imageFolder)) return null;
        return Path.GetFullPath(Path.Combine(imageFolder, stored));
    }

    /// <summary>매핑 저장소 키. 같은 폴더를 대소문자·끝 구분자 차이 없이 같게 본다.</summary>
    public static string FolderKey(string folder)
        => Path.TrimEndingDirectorySeparator(Path.GetFullPath(folder)).ToUpperInvariant();
}

/// <summary>패밀리 파일 하나 처리 결과.</summary>
public sealed record FamilyTypeImageResult(string FamilyPath, bool Succeeded, int TypeCount, string Message);
