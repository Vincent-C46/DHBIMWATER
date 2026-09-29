using System.Collections.Generic;

namespace DHBIMWATER.Core.Parameters
{
    public interface ISharedParameterRepository
    {
        /// <param name="extraCategoryIds">
        /// 정의가 나열한 <see cref="SharedParameterDefinition.Categories"/> 외에 추가로 바인딩할 카테고리.
        /// 값은 Revit <c>BuiltInCategory</c>의 정수값이다 — Core는 Revit API를 참조하지 않으므로 enum으로 받을 수 없다.
        /// <para>배치 대상 패밀리의 카테고리를 배치 직전에 읽어 넘기는 용도다(docs/141).
        /// 적응형 패밀리는 일반 모델이 아닐 수 있어 정적 목록만으로는 바인딩이 빈다.</para>
        /// </param>
        void EnsureParameters(IReadOnlyList<SharedParameterDefinition> definitions,
            IReadOnlyList<int>? extraCategoryIds = null);
    }
}
