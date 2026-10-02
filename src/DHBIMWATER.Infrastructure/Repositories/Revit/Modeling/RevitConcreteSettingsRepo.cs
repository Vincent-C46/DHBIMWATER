using System.Text.Json;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.ExtensibleStorage;
using DHBIMWATER.Application.Interfaces;
using DHBIMWATER.Core.Structures;
using DHBIMWATER.Shared.Errors;

namespace DHBIMWATER.Infrastructure.Repositories.Revit.Modeling;

/// <summary>공용 콘크리트 강도 설정을 ExtensibleStorage에 JSON 한 필드로 저장한다(docs/249 § C).
/// 패턴은 <c>RevitPipeLayoutFamilySettingsRepo</c>와 같다. Transaction은 열지 않는다 — UseCase 트랜잭션 안에서 호출된다.</summary>
public sealed class RevitConcreteSettingsRepo : IConcreteSettingsRepo
{
    private static readonly Guid SchemaGuid = new("F9725518-50B1-46C1-A5DB-0A611B50366F");
    private const string SchemaName = "DHBIMWATER_ConcreteSettings_V1";
    private const string StorageName = "DHBIMWATER_ConcreteSettings_V1";
    private const string SettingsField = "Settings";

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = false };

    private readonly Func<Document?> _doc;
    public RevitConcreteSettingsRepo(Func<Document?> doc) => _doc = doc;

    public ConcreteSettings? Load()
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
            var loaded = JsonSerializer.Deserialize<ConcreteSettings>(json, JsonOptions);
            return loaded is null ? null : FillMissing(loaded);
        }
        catch (JsonException)
        {
            // 손상된 저장본은 "없음"으로 본다 — 호출부가 기본값으로 시작한다.
            return null;
        }
    }

    public void Save(ConcreteSettings settings)
    {
        var doc = _doc() ?? throw new DhbimwaterException(ErrorCodes.NoActiveDocument, "활성 Revit 문서가 없습니다.");
        var schema = GetOrCreateSchema();
        var storage = FindStorage(doc) ?? CreateStorage(doc);

        var entity = new Entity(schema);
        entity.Set(schema.GetField(SettingsField), JsonSerializer.Serialize(settings, JsonOptions));
        storage.SetEntity(entity);
    }

    /// <summary>저장본에 없는 부재(이후 추가된 칸 등)는 기본값으로 채운다.</summary>
    private static ConcreteSettings FillMissing(ConcreteSettings loaded)
    {
        var result = loaded;
        foreach (var member in ConcreteSettings.Members)
        {
            if (loaded.Get(member) is null)
                result = result.With(member, ConcreteSettings.Default.Get(member));
        }
        return result;
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
