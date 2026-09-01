namespace DHBIMWATER.Core.Gis;

public enum ReducerAlignment { Concentric, Eccentric }

public sealed record ReducerSpec(double BodyLengthMm, ReducerAlignment Alignment);

public interface IReducerSpecSource
{
    ReducerSpec? Find(string pipeKind, double largeDiameterMm, double smallDiameterMm);
}

public sealed class FixedReducerSpecSource : IReducerSpecSource
{
    private readonly double _bodyLengthMm;
    private readonly ReducerAlignment _alignment;

    public FixedReducerSpecSource(double bodyLengthMm, ReducerAlignment alignment)
    { _bodyLengthMm = bodyLengthMm; _alignment = alignment; }

    public ReducerSpec? Find(string pipeKind, double largeDiameterMm, double smallDiameterMm)
        => _bodyLengthMm > 0d ? new ReducerSpec(_bodyLengthMm, _alignment) : null;
}
