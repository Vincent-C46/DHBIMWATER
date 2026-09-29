using System;
using System.Collections.Generic;

namespace DHBIMWATER.Core.Parameters
{
    /// <summary>밸브실 정보를 Revit "프로젝트 정보"에 남기는 매개변수 3종.
    /// <para>UI(입력·복원)와 UseCase(기록)가 같은 이름을 써야 하므로 이름은 여기 한 곳에서만 정의한다.
    /// GUID를 고정하는 이유는 <c>RevitSharedParameterRepository</c>가 공유 매개변수 파일을 매번 새로 쓰기 때문이다 —
    /// GUID를 비워 두면 호출할 때마다 새 GUID로 정의가 다시 만들어진다.</para></summary>
    public static class ValveRoomProjectInfoParameters
    {
        /// <summary>용도 유형 토글에서 고른 값(이토·공기·타이·제수밸브실)이 그대로 들어간다.</summary>
        public const string RoomType = "밸브실유형";
        /// <summary>사용자 입력. 예: 도수1-1</summary>
        public const string PipelineSection = "관로구간";
        /// <summary>사용자 입력. 예: 1+750.00</summary>
        public const string StationPoint = "밸브실측점";

        /// <summary>읽기·기록 대상 이름 목록(정의 순서와 같다).</summary>
        public static readonly IReadOnlyList<string> Names = new[] { RoomType, PipelineSection, StationPoint };

        /// <summary>프로젝트 정보 카테고리에 붙는 인스턴스 텍스트 매개변수 정의.
        /// 이미 있으면 <c>EnsureParameters</c>가 바인딩을 그대로 두므로 값만 갱신된다.</summary>
        public static IReadOnlyList<SharedParameterDefinition> Definitions { get; } = new List<SharedParameterDefinition>
        {
            Create(RoomType,        new Guid("3e1c8f42-7b95-4a16-9d0e-2c5f6a81b3d7")),
            Create(PipelineSection, new Guid("8a4d2b17-6c30-4f59-b1e2-7d93c04a6e58")),
            Create(StationPoint,    new Guid("c62f9048-15ae-4d73-8b6c-90e4a2517f3b")),
        };

        private static SharedParameterDefinition Create(string name, Guid guid) => new()
        {
            Name = name,
            Guid = guid,
            SpecType = ParameterSpecType.Text,
            GroupType = ParameterGroupType.Data,
            // 프로젝트 정보는 인스턴스 바인딩만 허용한다.
            BindingType = ParameterBindingType.Instance,
            Categories = new List<ParameterCategory> { ParameterCategory.ProjectInformation },
        };
    }
}
