using System.IO;
using System.Text.Json;
using DHBIMWATER.Application.Interfaces.Families;
using DHBIMWATER.Core.Families;

namespace DHBIMWATER.Infrastructure.Repositories.Local;

/// <summary>
/// 유형 이미지 매핑 저장소(docs/309 § 3-5). 패밀리 폴더 경로를 키로 한 JSON 한 파일.
/// <para>위치: <c>%LOCALAPPDATA%\DHBIMWATER\type-image-mappings.json</c>(<see cref="AdaptivePipeParameterNamesStore"/>와 같은 폴더).</para>
/// </summary>
internal sealed class TypeImageMappingStore : ITypeImageMappingStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private readonly string _filePath;

    public TypeImageMappingStore() : this(Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DHBIMWATER", "type-image-mappings.json"))
    {
    }

    internal TypeImageMappingStore(string filePath) => _filePath = filePath;

    public TypeImageMapping Load(string familyFolder)
    {
        var all = ReadAll();
        if (!all.TryGetValue(TypeImagePaths.FolderKey(familyFolder), out var dto) || dto is null) return TypeImageMapping.Empty;
        var images = new Dictionary<string, string>(dto.Images ?? [], StringComparer.OrdinalIgnoreCase);
        return new TypeImageMapping(dto.ImageFolder ?? "", images);
    }

    public void Save(string familyFolder, TypeImageMapping mapping)
    {
        var all = ReadAll();
        all[TypeImagePaths.FolderKey(familyFolder)] = new FolderDto
        {
            ImageFolder = mapping.ImageFolder,
            Images = mapping.Images.Where(x => !string.IsNullOrWhiteSpace(x.Value)).ToDictionary(x => x.Key, x => x.Value),
        };
        Directory.CreateDirectory(Path.GetDirectoryName(_filePath)!);
        File.WriteAllText(_filePath, JsonSerializer.Serialize(all, JsonOptions));
    }

    private Dictionary<string, FolderDto?> ReadAll()
    {
        try
        {
            if (!File.Exists(_filePath)) return new(StringComparer.OrdinalIgnoreCase);
            var all = JsonSerializer.Deserialize<Dictionary<string, FolderDto?>>(File.ReadAllText(_filePath), JsonOptions);
            return all is null ? new(StringComparer.OrdinalIgnoreCase) : new(all, StringComparer.OrdinalIgnoreCase);
        }
        catch (JsonException)
        {
            // 깨진 파일은 빈 매핑으로 시작. 저장하면 새로 씀.
            return new(StringComparer.OrdinalIgnoreCase);
        }
    }

    private sealed class FolderDto
    {
        public string? ImageFolder { get; set; }
        public Dictionary<string, string>? Images { get; set; }
    }
}
