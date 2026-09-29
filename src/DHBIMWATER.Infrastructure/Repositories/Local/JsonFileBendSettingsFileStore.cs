using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using DHBIMWATER.Application.Interfaces.Gis;
using DHBIMWATER.Core.Gis;

namespace DHBIMWATER.Infrastructure.Repositories.Local;

/// <summary>사용자가 선택한 JSON 파일에 관·곡관 설정 전체를 저장한다.</summary>
public sealed class JsonFileBendSettingsFileStore : IBendSettingsFileStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public BendSettings Load(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("설정 파일 경로가 비어 있습니다.", nameof(path));
        try
        {
            var dto = JsonSerializer.Deserialize<FileDto>(File.ReadAllText(path), JsonOptions)
                ?? throw new InvalidDataException("설정 파일의 내용이 비어 있습니다.");
            if (dto.StraightPipes is null || dto.JointDeflections is null || dto.Fittings is null)
                throw new InvalidDataException("직관·곡관·허용굴곡 설정이 모두 포함된 파일이 아닙니다.");

            // 형식(ActiveBendForm) 필드가 없는 구 파일은 구 ApplicationMode로 역매핑한다: 양쪽 Joint→A형, 그 외→B형(2026-09-03).
            var form = dto.ActiveBendForm ?? (dto.ApplicationMode == JointApplicationMode.BothJoints ? BendForm.AType : BendForm.BType);
            return new BendSettings(
                new StraightPipeSpecTable(dto.StraightPipes),
                new JointDeflectionTable(dto.JointDeflections),
                new BendFittingCatalog(dto.Fittings),
                string.IsNullOrWhiteSpace(dto.ActiveJointType) ? JointTypeCatalog.KpMechanical : dto.ActiveJointType,
                dto.ActiveBendConnection,
                form,
                dto.UseRestraintGland,
                // 강관 곡관 종류 필드가 없는 구 파일은 F12로 읽는다(docs/211 § C-8).
                dto.ActiveSteelFittingClass ?? SteelFittingClass.F12);
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException("관·곡관 설정 JSON 형식이 올바르지 않습니다.", ex);
        }
    }

    public void Save(string path, BendSettings settings)
    {
        if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("설정 파일 경로가 비어 있습니다.", nameof(path));
        ArgumentNullException.ThrowIfNull(settings);

        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory)) Directory.CreateDirectory(directory);
        var dto = new FileDto
        {
            StraightPipes = settings.StraightPipes.Entries.ToList(),
            JointDeflections = settings.JointDeflections.Entries.ToList(),
            Fittings = settings.Fittings.Entries.ToList(),
            ActiveJointType = settings.ActiveJointType,
            ApplicationMode = settings.ApplicationMode,
            ActiveBendConnection = settings.ActiveBendConnection,
            ActiveBendForm = settings.ActiveBendForm,
            UseRestraintGland = settings.UseRestraintGland,
            ActiveSteelFittingClass = settings.ActiveSteelFittingClass
        };
        File.WriteAllText(path, JsonSerializer.Serialize(dto, JsonOptions));
    }

    private sealed class FileDto
    {
        public List<StraightPipeSpec>? StraightPipes { get; set; }
        public List<JointDeflectionSpec>? JointDeflections { get; set; }
        public List<BendFittingEntry>? Fittings { get; set; }
        public string? ActiveJointType { get; set; }
        public JointApplicationMode ApplicationMode { get; set; }
        public BendConnection ActiveBendConnection { get; set; } = BendConnection.Socket;
        public BendForm? ActiveBendForm { get; set; }
        /// <summary>구 파일에는 없는 값이라 기본 false로 읽힌다(docs/166 § 11-4).</summary>
        public bool UseRestraintGland { get; set; }
        /// <summary>강관 곡관 종류(F12/F15/F20). 구 파일에는 없어 null → F12(docs/211 § C-8).</summary>
        public SteelFittingClass? ActiveSteelFittingClass { get; set; }
    }
}
