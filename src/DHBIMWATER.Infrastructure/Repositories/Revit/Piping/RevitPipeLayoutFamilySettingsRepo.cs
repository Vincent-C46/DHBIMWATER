using System.Text.Json;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.ExtensibleStorage;
using DHBIMWATER.Application.Interfaces;
using DHBIMWATER.Core.Piping;
using DHBIMWATER.Shared.Errors;

namespace DHBIMWATER.Infrastructure.Repositories.Revit.Piping;

/// <summary>PipeLayout 패밀리·파라미터 매핑을 ExtensibleStorage에 JSON 한 필드로 저장한다(docs/175 § E-3).
/// 패턴은 <c>RevitBendSettingsRepo</c>와 같다. Transaction은 열지 않는다 — UseCase 트랜잭션 안에서 호출된다.
/// <para>필드를 하나(JSON)로 두는 이유: Revit Schema는 만든 뒤 필드를 추가할 수 없다. 레코드에 칸이 늘어도
/// 스키마를 갈아엎지 않고 JSON만 넓히면 된다(구 저장본은 새 칸이 기본값으로 읽힌다).</para></summary>
public sealed class RevitPipeLayoutFamilySettingsRepo : IPipeLayoutFamilySettingsRepo
{
    private static readonly Guid SchemaGuid = new("6C2F0D3A-9B41-4E7A-8D25-3F1B7C9E5A10");
    private const string SchemaName = "DHBIMWATER_PipeLayoutFamilySettings_V1";
    private const string StorageName = "DHBIMWATER_PipeLayoutFamilySettings_V1";
    private const string SettingsField = "Settings";

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = false };

    private readonly Func<Document?> _doc;
    public RevitPipeLayoutFamilySettingsRepo(Func<Document?> doc) => _doc = doc;

    public PipeLayoutFamilySettings? Load()
    {
        var doc = _doc() ?? throw new DhbimwaterException(ErrorCodes.NoActiveDocument, "활성 Revit 문서가 없습니다.");
        var schema = Schema.Lookup(SchemaGuid);
        if (schema is null) return null;

        var storage = FindStorage(doc);
        if (storage is null) return null;

        var entity = storage.GetEntity(schema);
        if (!entity.IsValid()) return null;

        var json = entity.Get<string>(schema.GetField(SettingsField));
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            return JsonSerializer.Deserialize<PipeLayoutFamilySettings>(json, JsonOptions);
        }
        catch (JsonException)
        {
            // 손상된 저장본은 "없음"으로 본다 — 창을 못 여는 것보다 이름 추정으로 시작하는 편이 낫다.
            return null;
        }
    }

    public void Save(PipeLayoutFamilySettings settings)
    {
        var doc = _doc() ?? throw new DhbimwaterException(ErrorCodes.NoActiveDocument, "활성 Revit 문서가 없습니다.");
        var schema = GetOrCreateSchema();
        var storage = FindStorage(doc) ?? CreateStorage(doc);

        var entity = new Entity(schema);
        entity.Set(schema.GetField(SettingsField), JsonSerializer.Serialize(settings, JsonOptions));
        storage.SetEntity(entity);
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
        var schema = Schema.Lookup(SchemaGuid);
        if (schema != null) return schema;

        var builder = new SchemaBuilder(SchemaGuid);
        builder.SetSchemaName(SchemaName);
        builder.SetReadAccessLevel(AccessLevel.Public);
        builder.SetWriteAccessLevel(AccessLevel.Public);
        builder.AddSimpleField(SettingsField, typeof(string));
        return builder.Finish();
    }
}
