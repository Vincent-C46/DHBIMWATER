using Autodesk.Revit.DB;
using Autodesk.Revit.DB.ExtensibleStorage;

namespace DHBIMWATER.Infrastructure.Repositories.Revit.Piping;

/// <summary>
/// 격점상세도 시트에 붙이는 기록: 뷰 배치 규칙 버전.
/// <para>버전 다르면(이전 규칙으로 배치한 시트) 세 뷰포트 한 번 다시 배치. 같으면 사용자 위치 보존.</para>
/// <para>쓰기는 호출자 트랜잭션 안에서.</para>
/// </summary>
internal static class JunctionDetailSheetStorageSchema
{
    private static readonly Guid SchemaGuid = new("B4D17E2A-5C93-4A6F-8E21-7F0C3A9D5B16");
    private const string LayoutVersionField = "LayoutVersion";

    public static string? ReadLayoutVersion(Element sheet)
    {
        var schema = Schema.Lookup(SchemaGuid);
        if (schema is null) return null;
        var entity = sheet.GetEntity(schema);
        return entity.IsValid() ? entity.Get<string>(schema.GetField(LayoutVersionField)) : null;
    }

    public static void WriteLayoutVersion(Element sheet, string version)
    {
        var schema = GetOrCreate();
        var entity = new Entity(schema);
        entity.Set(schema.GetField(LayoutVersionField), version);
        sheet.SetEntity(entity);
    }

    private static Schema GetOrCreate()
    {
        var schema = Schema.Lookup(SchemaGuid);
        if (schema is not null) return schema;
        var builder = new SchemaBuilder(SchemaGuid);
        builder.SetSchemaName("DHBIMWATER_JunctionDetailSheet");
        builder.SetReadAccessLevel(AccessLevel.Public);
        builder.SetWriteAccessLevel(AccessLevel.Public);
        builder.AddSimpleField(LayoutVersionField, typeof(string));
        return builder.Finish();
    }
}
