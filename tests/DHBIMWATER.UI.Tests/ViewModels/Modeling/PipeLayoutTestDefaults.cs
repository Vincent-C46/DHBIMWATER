using DHBIMWATER.UI.ViewModels.Modeling;

namespace DHBIMWATER.UI.Tests.ViewModels.Modeling;

internal static class PipeLayoutTestDefaults
{
    public static PipeLayoutViewModel UseAbsoluteElevation(PipeLayoutViewModel viewModel)
    {
        viewModel.ElevationMode = DHBIMWATER.Core.Piping.PipeElevationMode.Absolute;
        return viewModel;
    }
}
