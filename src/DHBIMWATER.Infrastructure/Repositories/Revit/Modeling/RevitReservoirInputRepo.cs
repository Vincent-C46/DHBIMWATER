using Autodesk.Revit.DB;
using Autodesk.Revit.DB.ExtensibleStorage;
using DHBIMWATER.Application.Interfaces;
using DHBIMWATER.Shared.Errors;

namespace DHBIMWATER.Infrastructure.Repositories.Revit.Modeling;

/// <summary>배수지 창 입력값을 ExtensibleStorage에 JSON 한 필드로 저장. 문서당 1개(DataStorage 이름으로 찾음, docs/325 § E).
/// 패턴은 <c>RevitPumpingStationInputRepo</c>와 같음. Transaction 안 엶 — UseCase 트랜잭션 안에서 호출.</summary>
public sealed class RevitReservoirInputRepo : IReservoirInputRepo
{
    private static readonly Guid SchemaGuid = new("433B9785-7086-45E3-8B8A-A161EF87415B");
    private const string SchemaName = "DHBIMWATER_ReservoirInput_V1";
    private const string StorageName = "DHBIMWATER_ReservoirInput_V1";
    private const string InputField = "Input";

    private readonly Func<Document?> _doc;
    public RevitReservoirInputRepo(Func<Document?> doc) => _doc = doc;

    public string? Load()
    {
        var doc = _doc() ?? throw new DhbimwaterException(ErrorCodes.NoActiveDocument, "활성 Revit 문서가 없습니다.");
        var schema = Schema.Lookup(SchemaGuid);
        if (schema is null) return null;

        var storage = FindStorage(doc);
        if (storage is null) return null;

        var entity = storage.GetEntity(schema);
        if (!entity.IsValid()) return null;

        var json = entity.Get<string>(schema.GetField(InputField));
        return string.IsNullOrWhiteSpace(json) ? null : json;
    }

    public void Save(string json)
    {
        var doc = _doc() ?? throw new DhbimwaterException(ErrorCodes.NoActiveDocument, "활성 Revit 문서가 없습니다.");
        var schema = GetOrCreateSchema();
        var storage = FindStorage(doc) ?? CreateStorage(doc);

        var entity = new Entity(schema);
        entity.Set(schema.GetField(InputField), json);
        storage.SetEntity(entity);
    }

    private static DataStorage? FindStorage(Document doc)
        => new FilteredElementCollector(doc)
            .OfClass(typeof(DataStorage))
            .Cast<DataStorage>()
            .FirstOrDefault(x => string.Equals(x.Name, StorageName, StringComparison.Ordinal));

    /// <summary>DataStorage 생성도 호출부 트랜잭션 안.</summary>
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
        builder.AddSimpleField(InputField, typeof(string));
        return builder.Finish();
    }
}
