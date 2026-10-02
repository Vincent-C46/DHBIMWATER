using DHBIMWATER.Application.Interfaces;
using DHBIMWATER.Core.Structures;

namespace DHBIMWATER.Application.Services;

/// <summary>
/// 공용 콘크리트 설정(docs/249)으로 재료·바닥·벽 유형을 정한다. 구조물 UseCase가 트랜잭션 안에서 생성해 쓴다.
/// 재료는 콘크리트 사양별로 한 번만 찾는다. 실패(0) 판정과 오류 메시지는 호출 UseCase가 한다.
/// </summary>
public sealed class ConcreteTypeResolver
{
    private readonly IElementTypeCommandRepo _repo;
    private readonly Dictionary<ConcreteSpec, int> _materials = new();

    public ConcreteTypeResolver(IElementTypeCommandRepo repo, ConcreteSettings settings)
    {
        _repo = repo;
        Settings = settings;
    }

    public ConcreteSettings Settings { get; }

    public int MaterialId(ConcreteMember member) => MaterialId(Settings.Get(member));

    public int MaterialId(ConcreteSpec concrete)
    {
        if (!_materials.TryGetValue(concrete, out var id))
            _materials[concrete] = id = _repo.FindOrCreateConcreteMaterial(concrete);
        return id;
    }

    /// <summary>
    /// 바닥마다 Part로 부재(슬래브·기초·버림)를 정하고 유형을 만든다. 결과는 <paramref name="slabs"/>와 같은 순서다.
    /// 같은 두께에 강도가 섞이면 유형 이름에 "_{강도}MPa"가 붙는다(<see cref="ConcreteSettings.ResolveTypeNames"/>).
    /// </summary>
    public IReadOnlyList<ResolvedConcreteType> ResolveSlabTypes(IReadOnlyList<SlabDefinition> slabs)
    {
        var usages = slabs.Select(s => (s.Thickness, Concrete: Settings.Get(ConcreteSettings.ClassifySlabPart(s.Part)))).ToList();
        var names = ConcreteSettings.ResolveTypeNames(usages);
        var types = new Dictionary<(double, ConcreteSpec), ResolvedConcreteType>();
        foreach (var usage in usages.Distinct())
        {
            var materialId = MaterialId(usage.Concrete);
            var name = names[usage];
            var typeId = materialId == 0 ? 0 : _repo.FindOrCreateSlabType(new FloorTypeSpec(usage.Thickness, name, usage.Concrete), materialId);
            types[usage] = new ResolvedConcreteType(name, typeId, materialId);
        }
        return usages.Select(u => types[u]).ToList();
    }

    /// <summary>벽은 부재가 하나(벽체)라 한 번의 생성에서 이름이 겹치지 않는다 — "일반 - {t}mm".</summary>
    public IReadOnlyDictionary<double, ResolvedConcreteType> ResolveWallTypes(IEnumerable<double> thicknesses)
    {
        var concrete = Settings.Wall;
        var materialId = MaterialId(concrete);
        var result = new Dictionary<double, ResolvedConcreteType>();
        foreach (var thickness in thicknesses.Distinct())
        {
            var name = ConcreteSettings.ResolveTypeNames(new[] { (thickness, concrete) })[(thickness, concrete)];
            var typeId = materialId == 0 ? 0 : _repo.FindOrCreateWallType(new WallTypeSpec(thickness, name, concrete), materialId);
            result[thickness] = new ResolvedConcreteType(name, typeId, materialId);
        }
        return result;
    }

    /// <summary>보·기둥·독립기초 유형(또는 인스턴스)의 구조 재료를 부재 강도로 바꾼다. 매개변수가 없으면 Repo가 로그만 남긴다.</summary>
    public void ApplyTo(int elementOrTypeId, ConcreteMember member)
    {
        if (elementOrTypeId == 0) return;
        var materialId = MaterialId(member);
        if (materialId != 0) _repo.ApplyStructuralMaterial(elementOrTypeId, materialId);
    }
}

public sealed record ResolvedConcreteType(string TypeName, int TypeId, int MaterialId);
