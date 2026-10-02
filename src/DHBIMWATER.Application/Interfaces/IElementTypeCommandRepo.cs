using DHBIMWATER.Core.Structures;

namespace DHBIMWATER.Application.Interfaces
{
    public interface IElementTypeCommandRepo
    {
        int FindOrCreateConcreteMaterial(ConcreteSpec concrete);
        int FindOrCreateSlabType(FloorTypeSpec spec, int materialId);
        int FindOrCreateWallType(WallTypeSpec spec, int materialId);
        int FindOrCreateBeamType(BeamTypeSpec spec);
        int FindBeamSymbol(string typeName);
        /// <summary>"패밀리명 : 타입명"으로 구조 프레임 유형을 찾는다. 구분자가 없으면 타입명 단독으로 보고
        /// <see cref="FindBeamSymbol"/>과 같게 동작한다(하위호환). 찾지 못하면 0(docs/156 § E-1).</summary>
        int FindStructuralFramingSymbolByFamilyType(string familyTypeName);
        int FindHaunchBeamSymbol();
        int FindColumnSymbol(string typeName);
        /// <summary>"패밀리명 : 타입명"으로 구조 기둥 유형을 찾는다. 구분자가 없으면 타입명 단독으로 보고
        /// <see cref="FindColumnSymbol"/>과 같게 동작한다(하위호환). 찾지 못하면 0(docs/159 § C-1).</summary>
        int FindColumnSymbolByFamilyType(string familyTypeName);
        int FindGenericModelSymbol(string symbolName);
        /// <summary>유형(또는 인스턴스의 유형)의 구조 재료를 materialId로 바꾼다. 매개변수가 없거나 읽기 전용이면 false(docs/249 § B).
        /// 유형을 쓰는 문서 전체 인스턴스에 반영된다.</summary>
        bool ApplyStructuralMaterial(int elementOrTypeId, int materialId);
    }
}
