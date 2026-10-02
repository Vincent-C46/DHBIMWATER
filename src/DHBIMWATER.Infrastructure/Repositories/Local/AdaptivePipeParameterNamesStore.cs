using System.IO;
using System.Text.Json;
using DHBIMWATER.Core.Piping;

namespace DHBIMWATER.Infrastructure.Repositories.Local;

/// <summary>
/// 직접 배치 가변 패밀리 자동입력의 매개변수 이름 목록(docs/267 § 2-2)을 JSON으로 읽는다.
/// <para>위치: <c>%LOCALAPPDATA%\DHBIMWATER\adaptive-pipe-parameter-names.json</c>(로그·계측 파일과 같은 폴더).
/// 파일이 없으면 기본값으로 만들어 두어 사용자가 열어 고칠 수 있게 한다. 읽지 못하면 기본값을 쓴다 — 재기록을 막지 않는다.</para>
/// <para>UI는 아직 없다(docs/267 § 3).</para>
/// </summary>
internal static class AdaptivePipeParameterNamesStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public static string FilePath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DHBIMWATER", "adaptive-pipe-parameter-names.json");

    /// <param name="warning">파일을 읽지 못해 기본값을 쓴 경우의 안내. 정상이면 null.</param>
    public static AdaptivePipeParameterNames Load(out string? warning)
    {
        warning = null;
        try
        {
            if (!File.Exists(FilePath))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
                File.WriteAllText(FilePath, JsonSerializer.Serialize(ToDto(AdaptivePipeParameterNames.Default), JsonOptions));
                return AdaptivePipeParameterNames.Default;
            }
            var dto = JsonSerializer.Deserialize<FileDto>(File.ReadAllText(FilePath), JsonOptions);
            var fallback = AdaptivePipeParameterNames.Default;
            return new AdaptivePipeParameterNames(
                Clean(dto?.Length) ?? fallback.Length,
                Clean(dto?.Material) ?? fallback.Material,
                Clean(dto?.NominalDiameter) ?? fallback.NominalDiameter,
                Clean(dto?.BendAngle) ?? fallback.BendAngle);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            warning = $"가변 패밀리 매개변수 이름 설정({FilePath})을 읽지 못해 기본값을 썼습니다: {ex.Message}";
            return AdaptivePipeParameterNames.Default;
        }
    }

    private static IReadOnlyList<string>? Clean(List<string>? names)
    {
        var list = names?.Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x.Trim()).ToList();
        return list is { Count: > 0 } ? list : null;
    }

    private static FileDto ToDto(AdaptivePipeParameterNames names)
        => new() { Length = [.. names.Length], Material = [.. names.Material], NominalDiameter = [.. names.NominalDiameter], BendAngle = [.. names.BendAngle] };

    private sealed class FileDto
    {
        public List<string>? Length { get; set; }
        public List<string>? Material { get; set; }
        public List<string>? NominalDiameter { get; set; }
        public List<string>? BendAngle { get; set; }
    }
}
