using Autodesk.Revit.DB;
using Autodesk.Revit.DB.ExtensibleStorage;

namespace DHBIMWATER.Infrastructure.Repositories.Revit.Piping;

/// <summary>
/// 재료표 드래프팅뷰에 붙이는 기록(docs/291): 표 서명 + 애드인이 그린 요소 Id.
/// <para>서명 같으면 다시 안 그림. 다시 그릴 때는 기록된 요소만 지움 → 사용자가 덧붙인 치수·주석 보존.</para>
/// <para>엔티티는 드래프팅뷰 요소 자체에 붙인다. 쓰기는 호출자 트랜잭션 안에서.</para>
/// </summary>
internal static class PipeMaterialDraftingStorageSchema
{
    private static readonly Guid SchemaGuid = new("6E2B9C41-7D3A-4F58-9A1E-3C5B8D0F2A74");
    private const string SignatureField = "Signature";
    private const string ElementIdsField = "ElementIds";

    public sealed record Record(string Signature, IReadOnlyList<ElementId> ElementIds);

    public static Record? Read(Element view)
    {
        var schema = Schema.Lookup(SchemaGuid);
        if (schema is null) return null;
        var entity = view.GetEntity(schema);
        if (!entity.IsValid()) return null;
        return new Record(entity.Get<string>(schema.GetField(SignatureField)) ?? string.Empty,
            entity.Get<IList<ElementId>>(schema.GetField(ElementIdsField))?.ToList() ?? []);
    }

    public static void Write(Element view, string signature, IEnumerable<ElementId> elementIds)
    {
        var schema = GetOrCreate();
        var entity = new Entity(schema);
        entity.Set(schema.GetField(SignatureField), signature);
        entity.Set<IList<ElementId>>(schema.GetField(ElementIdsField), elementIds.ToList());
        view.SetEntity(entity);
    }

    private static Schema GetOrCreate()
    {
        var schema = Schema.Lookup(SchemaGuid);
        if (schema is not null) return schema;
        var builder = new SchemaBuilder(SchemaGuid);
        builder.SetSchemaName("DHBIMWATER_PipeMaterialDrafting");
        builder.SetReadAccessLevel(AccessLevel.Public);
        builder.SetWriteAccessLevel(AccessLevel.Public);
        builder.AddSimpleField(SignatureField, typeof(string));
        builder.AddArrayField(ElementIdsField, typeof(ElementId));
        return builder.Finish();
    }
}
