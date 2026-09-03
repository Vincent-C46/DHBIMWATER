using DHBIMWATER.Application.DTOs.Gis;

namespace DHBIMWATER.Application.Interfaces.Gis;

public interface IPipeAlignmentCommandRepo
{
    /// <summary>이전에 만든 선형 미리보기 DirectShape를 지운다. 지운 개수를 반환한다.</summary>
    int DeletePreviews();
    PipeAlignmentCreateResult Create(PipeAlignmentCreateDefinition definition, IProgress<PipeAlignmentProgress>? progress = null);
}
