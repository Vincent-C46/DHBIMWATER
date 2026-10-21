using System.Globalization;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.ExtensibleStorage;

namespace DHBIMWATER.Infrastructure.Repositories.Revit.Piping;

/// <summary>
/// 격점상세도가 심볼 방향 맞추려 회전한 부재의 원래 방향(docs/306): BasisY 월드 평면 각(라디안).
/// <para>기록 있으면 애드인이 돌린 부재 → 초기화 때 이 각으로 되돌리고 기록 지움.</para>
/// <para>쓰기는 호출자 트랜잭션 안에서.</para>
/// </summary>
internal static class JunctionRotationStorageSchema
{
    private static readonly Guid SchemaGuid = new("6F2C8D41-A93E-4B57-9D0E-3B7A15C2E864");
    private const string AngleField = "OriginalAngle";

    public static double? Read(Element element)
    {
        var schema = Schema.Lookup(SchemaGuid);
        if (schema is null) return null;
        var entity = element.GetEntity(schema);
        if (!entity.IsValid()) return null;
        return double.TryParse(entity.Get<string>(schema.GetField(AngleField)), NumberStyles.Float,
            CultureInfo.InvariantCulture, out var angle) ? angle : null;
    }

    public static void Write(Element element, double angle)
    {
        var schema = GetOrCreate();
        var entity = new Entity(schema);
        // 실수 필드는 단위 지정 필요 → 문자열(InvariantCulture)
        entity.Set(schema.GetField(AngleField), angle.ToString("R", CultureInfo.InvariantCulture));
        element.SetEntity(entity);
    }

    public static void Clear(Element element)
    {
        var schema = Schema.Lookup(SchemaGuid);
        if (schema is not null && element.GetEntity(schema).IsValid()) element.DeleteEntity(schema);
    }

    private static Schema GetOrCreate()
    {
        var schema = Schema.Lookup(SchemaGuid);
        if (schema is not null) return schema;
        var builder = new SchemaBuilder(SchemaGuid);
        builder.SetSchemaName("DHBIMWATER_JunctionRotation");
        builder.SetReadAccessLevel(AccessLevel.Public);
        builder.SetWriteAccessLevel(AccessLevel.Public);
        builder.AddSimpleField(AngleField, typeof(string));
        return builder.Finish();
    }
}
