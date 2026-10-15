using System.Globalization;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.ExtensibleStorage;

namespace DHBIMWATER.Infrastructure.Repositories.Revit.Piping;

/// <summary>
/// 격점상세도 번호 태그에 붙이는 기록(docs/296): 애드인이 마지막에 둔 태그 머리·지시선 끝(모델 좌표, ft).
/// <para>재실행 때 현재 위치가 기록과 같으면 애드인 소유 → 새 위치로 옮김. 다르면 사용자가 옮긴 것 → 그대로.</para>
/// <para>쓰기는 호출자 트랜잭션 안에서.</para>
/// </summary>
internal static class JunctionTagStorageSchema
{
    private static readonly Guid SchemaGuid = new("B4E1F7A2-3C86-4D9B-8E25-7A1D0C6F93E8");
    private const string HeadField = "Head";
    private const string LeaderEndField = "LeaderEnd";

    public sealed record Record(XYZ Head, XYZ? LeaderEnd);

    public static Record? Read(Element tag)
    {
        var schema = Schema.Lookup(SchemaGuid);
        if (schema is null) return null;
        var entity = tag.GetEntity(schema);
        if (!entity.IsValid()) return null;
        var head = Parse(entity.Get<string>(schema.GetField(HeadField)));
        return head is null ? null : new Record(head, Parse(entity.Get<string>(schema.GetField(LeaderEndField))));
    }

    public static void Write(Element tag, XYZ head, XYZ? leaderEnd)
    {
        var schema = GetOrCreate();
        var entity = new Entity(schema);
        entity.Set(schema.GetField(HeadField), Format(head));
        entity.Set(schema.GetField(LeaderEndField), leaderEnd is null ? string.Empty : Format(leaderEnd));
        tag.SetEntity(entity);
    }

    // 실수 필드는 단위 지정 필요 → 문자열 "x;y;z"(ft, InvariantCulture)로 둠
    private static string Format(XYZ p)
        => string.Join(";", new[] { p.X, p.Y, p.Z }.Select(x => x.ToString("R", CultureInfo.InvariantCulture)));

    private static XYZ? Parse(string? text)
    {
        var parts = (text ?? string.Empty).Split(';');
        if (parts.Length != 3) return null;
        var values = new double[3];
        for (var i = 0; i < 3; i++)
            if (!double.TryParse(parts[i], NumberStyles.Float, CultureInfo.InvariantCulture, out values[i])) return null;
        return new XYZ(values[0], values[1], values[2]);
    }

    private static Schema GetOrCreate()
    {
        var schema = Schema.Lookup(SchemaGuid);
        if (schema is not null) return schema;
        var builder = new SchemaBuilder(SchemaGuid);
        builder.SetSchemaName("DHBIMWATER_JunctionTag");
        builder.SetReadAccessLevel(AccessLevel.Public);
        builder.SetWriteAccessLevel(AccessLevel.Public);
        builder.AddSimpleField(HeadField, typeof(string));
        builder.AddSimpleField(LeaderEndField, typeof(string));
        return builder.Finish();
    }
}
