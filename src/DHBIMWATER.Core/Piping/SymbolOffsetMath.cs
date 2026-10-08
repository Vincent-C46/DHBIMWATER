namespace DHBIMWATER.Core.Piping;

public readonly record struct SymbolVector(double X, double Y, double Z);

public static class SymbolOffsetMath
{
    public static SymbolVector ToLocal(SymbolVector world, SymbolVector basisX, SymbolVector basisY, SymbolVector basisZ)
    {
        var yz = Cross(basisY, basisZ);
        var zx = Cross(basisZ, basisX);
        var xy = Cross(basisX, basisY);
        var determinant = Dot(basisX, yz);
        if (Math.Abs(determinant) < 1e-12)
            throw new ArgumentException("인스턴스 변환의 기저가 유효하지 않습니다.");
        return new SymbolVector(Dot(world, yz) / determinant,
            Dot(world, zx) / determinant, Dot(world, xy) / determinant);
    }

    private static double Dot(SymbolVector a, SymbolVector b)
        => a.X * b.X + a.Y * b.Y + a.Z * b.Z;

    private static SymbolVector Cross(SymbolVector a, SymbolVector b)
        => new(a.Y * b.Z - a.Z * b.Y, a.Z * b.X - a.X * b.Z, a.X * b.Y - a.Y * b.X);
}
