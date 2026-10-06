using System.Text.Json;
using System.Text.Json.Serialization;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.ExtensibleStorage;
using DHBIMWATER.Application.Interfaces.Gis;
using DHBIMWATER.Shared.Errors;
using DHBIMWATER.Core.Piping;

namespace DHBIMWATER.Infrastructure.Repositories.Revit.Gis;

/// <summary>
/// 직관 제원·Joint 허용굴곡·곡관 치수와 판정 옵션을 ExtensibleStorage로 저장한다.
/// Transaction은 열지 않는다 — 반드시 UseCase 트랜잭션 안에서 호출된다.
/// </summary>
public sealed class RevitBendSettingsRepo : IBendSettingsRepo
{
    // V1은 관종 기반 허용굴곡이라 의미가 달라 마이그레이션하지 않는다.
    private static readonly Guid BendSettingsSchemaGuidV2 = new("B42E5D80-1D2F-4AE0-9D12-7A4F63C9E281");
    private const string SchemaName = "DHBIMWATER_BendSettings_V2";
    private const string StorageName = "DHBIMWATER_BendSettings_V2";
    private const string StraightPipeField = "StraightPipeSpecs";
    private const string JointDeflectionField = "JointDeflections";
    private const string FittingField = "Fittings";
    private const string ActiveJointTypeField = "ActiveJointType";
    private const string ApplicationModeField = "ApplicationMode";

    /// <summary>BendForm을 "AType"처럼 이름으로 남겨 스키마 변경에 견디게 한다.</summary>
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly Func<Document?> _doc;
    public RevitBendSettingsRepo(Func<Document?> doc) => _doc = doc;

    public BendSettings? Load()
    {
        var doc = _doc() ?? throw new DhbimwaterException(ErrorCodes.NoActiveDocument, "활성 Revit 문서가 없습니다.");
        var schema = Schema.Lookup(BendSettingsSchemaGuidV2);
        if (schema is null) return null;

        var storage = FindStorage(doc);
        if (storage is null) return null;

        var entity = storage.GetEntity(schema);
        if (!entity.IsValid()) return null;

        var straight = Deserialize<StraightPipeSpec>(entity.Get<string>(schema.GetField(StraightPipeField)));
        var joints = Deserialize<JointDeflectionSpec>(entity.Get<string>(schema.GetField(JointDeflectionField)));
        var fittings = Deserialize<BendFittingEntry>(entity.Get<string>(schema.GetField(FittingField)));
        var activeJointType = entity.Get<string>(schema.GetField(ActiveJointTypeField));
        var (form, connection, restraintGland, steelClass, steelMode) = ParseModes(entity.Get<string>(schema.GetField(ApplicationModeField)));
        return new BendSettings(new StraightPipeSpecTable(straight), new JointDeflectionTable(joints),
            new BendFittingCatalog(fittings), string.IsNullOrWhiteSpace(activeJointType) ? JointTypeCatalog.KpMechanical : activeJointType, connection, form, restraintGland, steelClass, steelMode);
    }

    public void Save(BendSettings settings)
    {
        var doc = _doc() ?? throw new DhbimwaterException(ErrorCodes.NoActiveDocument, "활성 Revit 문서가 없습니다.");
        var schema = GetOrCreateSchema();
        var storage = FindStorage(doc) ?? CreateStorage(doc);

        var entity = new Entity(schema);
        entity.Set(schema.GetField(StraightPipeField), "[]");
        entity.Set(schema.GetField(JointDeflectionField), "[]");
        entity.Set(schema.GetField(FittingField), "[]");
        entity.Set(schema.GetField(ActiveJointTypeField), settings.ActiveJointType);
        // 기존 Schema에 필드를 추가할 수 없으므로 같은 문자열 필드에 함께 저장한다. 구 값("SingleJoint")도 ParseModes가 그대로 읽는다.
        // 5번째 토큰 = 강관 곡관 종류(docs/211 § C-8). 4토큰 구 저장본은 ParseModes가 F12로 읽는다.
        // 6번째 토큰 = 강관 곡관 접합 적용(docs/243 § B). 5토큰 구 저장본은 편측으로 읽는다.
        entity.Set(schema.GetField(ApplicationModeField), BuildApplicationMode(settings));
        storage.SetEntity(entity);
    }

    public void SavePlacementSettings(BendSettings settings)
    {
        var doc = _doc() ?? throw new DhbimwaterException(ErrorCodes.NoActiveDocument, "활성 Revit 문서가 없습니다.");
        var schema = GetOrCreateSchema();
        var storage = FindStorage(doc) ?? CreateStorage(doc);
        var existing = storage.GetEntity(schema);
        var entity = new Entity(schema);

        entity.Set(schema.GetField(StraightPipeField), GetExistingTable(existing, schema, StraightPipeField));
        entity.Set(schema.GetField(JointDeflectionField), GetExistingTable(existing, schema, JointDeflectionField));
        entity.Set(schema.GetField(FittingField), GetExistingTable(existing, schema, FittingField));
        entity.Set(schema.GetField(ActiveJointTypeField), settings.ActiveJointType);
        entity.Set(schema.GetField(ApplicationModeField), BuildApplicationMode(settings));
        storage.SetEntity(entity);
    }

    private static string GetExistingTable(Entity existing, Schema schema, string fieldName)
        => existing.IsValid() ? existing.Get<string>(schema.GetField(fieldName)) ?? "[]" : "[]";

    private static string BuildApplicationMode(BendSettings settings)
        => $"{settings.ApplicationMode}|{settings.ActiveBendConnection}|{settings.ActiveBendForm}|{settings.UseRestraintGland}|{settings.ActiveSteelFittingClass}|{settings.SteelApplicationMode}";

    private static List<T> Deserialize<T>(string? json)
        => string.IsNullOrWhiteSpace(json) ? new List<T>() : JsonSerializer.Deserialize<List<T>>(json, JsonOptions) ?? new List<T>();

    /// <summary>구 저장본(형식 필드 없음)은 ApplicationMode로 형식을 역매핑한다: 양쪽 Joint→A형, 그 외→B형(2026-09-03).</summary>
    private static (BendForm Form, BendConnection Connection, bool UseRestraintGland, SteelFittingClass SteelFittingClass, JointApplicationMode SteelApplicationMode) ParseModes(string? text)
    {
        var parts = (text ?? string.Empty).Split('|');
        var mode = Enum.TryParse<JointApplicationMode>(parts[0], out var parsedMode) ? parsedMode : JointApplicationMode.SingleJoint;
        var connection = parts.Length > 1 && Enum.TryParse<BendConnection>(parts[1], out var parsedConnection)
            ? parsedConnection
            : BendConnection.Socket;
        var form = parts.Length > 2 && Enum.TryParse<BendForm>(parts[2], out var parsedForm)
            ? parsedForm
            : (mode == JointApplicationMode.BothJoints ? BendForm.AType : BendForm.BType);
        // 4번째 토큰이 없는 구 저장본은 이탈방지압륜 미적용으로 읽는다 — 기존 모델의 KP접합수를 건드리지 않기 위해서다.
        var restraintGland = parts.Length > 3 && bool.TryParse(parts[3], out var parsedGland) && parsedGland;
        // 5번째 토큰이 없는 구 저장본은 강관 곡관 종류 F12로 읽는다(docs/211 § C-8).
        var steelClass = parts.Length > 4 && Enum.TryParse<SteelFittingClass>(parts[4], out var parsedClass) && parsedClass != SteelFittingClass.None
            ? parsedClass
            : SteelFittingClass.F12;
        // 6번째 토큰이 없는 구 저장본은 강관 곡관 편측으로 읽는다 — 기존 강관 곡관의 슬롯 수 1을 지킨다(docs/243 § B).
        var steelMode = parts.Length > 5 && Enum.TryParse<JointApplicationMode>(parts[5], out var parsedSteelMode)
            ? parsedSteelMode
            : JointApplicationMode.SingleJoint;
        return (form, connection, restraintGland, steelClass, steelMode);
    }

    private static DataStorage? FindStorage(Document doc)
        => new FilteredElementCollector(doc)
            .OfClass(typeof(DataStorage))
            .Cast<DataStorage>()
            .FirstOrDefault(x => string.Equals(x.Name, StorageName, StringComparison.Ordinal));

    /// <summary>DataStorage 생성도 호출부 트랜잭션 안에서 일어난다.</summary>
    private static DataStorage CreateStorage(Document doc)
    {
        var storage = DataStorage.Create(doc);
        storage.Name = StorageName;
        return storage;
    }

    private static Schema GetOrCreateSchema()
    {
        var schema = Schema.Lookup(BendSettingsSchemaGuidV2);
        if (schema != null) return schema;

        var builder = new SchemaBuilder(BendSettingsSchemaGuidV2);
        builder.SetSchemaName(SchemaName);
        builder.SetReadAccessLevel(AccessLevel.Public);
        builder.SetWriteAccessLevel(AccessLevel.Public);
        builder.AddSimpleField(StraightPipeField, typeof(string));
        builder.AddSimpleField(JointDeflectionField, typeof(string));
        builder.AddSimpleField(FittingField, typeof(string));
        builder.AddSimpleField(ActiveJointTypeField, typeof(string));
        builder.AddSimpleField(ApplicationModeField, typeof(string));
        return builder.Finish();
    }
}
