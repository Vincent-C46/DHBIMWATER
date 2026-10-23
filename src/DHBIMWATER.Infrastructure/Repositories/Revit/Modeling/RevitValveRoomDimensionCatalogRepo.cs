using System.Text.Json;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.ExtensibleStorage;
using DHBIMWATER.Application.Interfaces;
using DHBIMWATER.Core.Structures;
using DHBIMWATER.Shared.Errors;

namespace DHBIMWATER.Infrastructure.Repositories.Revit.Modeling;

/// <summary>밸브실 제원표 편집본을 ExtensibleStorage에 JSON 한 필드로 저장한다(docs/312).
/// 패턴은 <see cref="RevitConcreteSettingsRepo"/>와 같다. Transaction은 열지 않는다 — UseCase 트랜잭션 안에서 호출된다.</summary>
public sealed class RevitValveRoomDimensionCatalogRepo : IValveRoomDimensionCatalogRepo
{
    private static readonly Guid SchemaGuid = new("3B8E5C21-7D4A-4F6E-9A12-6C0D8E4B7F35");
    private const string SchemaName = "DHBIMWATER_ValveRoomDimensionCatalog_V1";
    private const string StorageName = "DHBIMWATER_ValveRoomDimensionCatalog_V1";
    private const string TableField = "Table";

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = false };

    private readonly Func<Document?> _doc;
    public RevitValveRoomDimensionCatalogRepo(Func<Document?> doc) => _doc = doc;

    public ValveRoomDimensionTable? Load()
    {
        var doc = _doc() ?? throw new DhbimwaterException(ErrorCodes.NoActiveDocument, "활성 Revit 문서가 없습니다.");
        var schema = Schema.Lookup(SchemaGuid);
        if (schema is null) return null;

        var storage = FindStorage(doc);
        if (storage is null) return null;

        var entity = storage.GetEntity(schema);
        if (!entity.IsValid()) return null;

        var json = entity.Get<string>(schema.GetField(TableField));
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            var rows = JsonSerializer.Deserialize<List<Row>>(json, JsonOptions);
            if (rows is null) return null;
            return new ValveRoomDimensionTable(rows
                .Where(x => !string.IsNullOrWhiteSpace(x.RoomKind))
                .Select(x => new ValveRoomStandardDimension(x.RoomKind!, x.Variant, x.Group, x.DiameterMm, x.LengthMm, x.WidthMm, x.HeightMm, x.Section ?? string.Empty)));
        }
        catch (JsonException)
        {
            // 손상된 저장본은 "없음"으로 본다 — 호출부가 내장 표준표로 시작한다.
            return null;
        }
    }

    public void Save(ValveRoomDimensionTable table)
    {
        var doc = _doc() ?? throw new DhbimwaterException(ErrorCodes.NoActiveDocument, "활성 Revit 문서가 없습니다.");
        var schema = GetOrCreateSchema();
        var storage = FindStorage(doc) ?? CreateStorage(doc);

        var rows = table.Entries.Select(x => new Row
        {
            RoomKind = x.RoomKind, Variant = x.Variant, Group = x.Group, DiameterMm = x.DiameterMm,
            LengthMm = x.LengthMm, WidthMm = x.WidthMm, HeightMm = x.HeightMm, Section = x.Section,
        }).ToList();
        var entity = new Entity(schema);
        entity.Set(schema.GetField(TableField), JsonSerializer.Serialize(rows, JsonOptions));
        storage.SetEntity(entity);
    }

    /// <summary>저장 형식. Core 레코드와 분리해 둬서 레코드가 바뀌어도 옛 저장본을 읽는다.</summary>
    private sealed class Row
    {
        public string? RoomKind { get; set; }
        public string? Variant { get; set; }
        public int Group { get; set; }
        public double DiameterMm { get; set; }
        public double LengthMm { get; set; }
        public double WidthMm { get; set; }
        public double HeightMm { get; set; }
        public string? Section { get; set; }
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
        builder.AddSimpleField(TableField, typeof(string));
        return builder.Finish();
    }
}
