namespace DHBIMWATER.Core.Piping;

public sealed class PipeNetworkBuildException : InvalidOperationException
{
    public IReadOnlyList<PipeConnectionFailure> Failures { get; }

    public PipeNetworkBuildException(string message, IReadOnlyList<PipeConnectionFailure> failures) : base(message)
        => Failures = failures;
}
