using DHBIMWATER.Application.DTOs.Gis;
using DHBIMWATER.Application.Interfaces.Gis;
using DHBIMWATER.Core.Gis;
using System.IO;
using DHBIMWATER.Shared.Errors;

namespace DHBIMWATER.Infrastructure.Repositories.Gis;

public sealed class ShapefileReader : IShapefileReader, IAlignmentSourceReader
{
    public bool CanRead(string filePath) => string.Equals(Path.GetExtension(filePath), ".shp", StringComparison.OrdinalIgnoreCase);

    public ShapefileReadResult Read(string shpPath)
    {
        if (!File.Exists(shpPath)) throw new DhbimwaterException(ErrorCodes.PA_ShpFileMissing, $"SHP 파일을 찾을 수 없습니다: {shpPath}");
        var dbfPath = Path.ChangeExtension(shpPath, ".dbf");
        if (!File.Exists(dbfPath)) throw new DhbimwaterException(ErrorCodes.PA_ShpFileMissing, $"SHP 형제 .dbf 파일이 필요합니다: {dbfPath}");
        var warnings = new List<string>();
        var cpgPath = Path.ChangeExtension(shpPath, ".cpg");
        var prjPath = Path.ChangeExtension(shpPath, ".prj");
        if (!File.Exists(cpgPath)) warnings.Add(".cpg 없음 — CP949로 해석");
        if (!File.Exists(prjPath)) warnings.Add(".prj 없음 — 좌표계 미확인");
        var shp = ShpGeometryReader.Read(shpPath);
        var dbf = DbfTableReader.Read(dbfPath, cpgPath);
        var prj = PrjFileReader.Read(prjPath);
        if (shp.Records.Count != dbf.Rows.Count) warnings.Add($"SHP {shp.Records.Count}건 / DBF {dbf.Rows.Count}건 불일치 — 최소 건수만 매칭");
        var features = new List<PipeAlignment>();
        foreach (var pair in shp.Records.Take(Math.Min(shp.Records.Count, dbf.Rows.Count)).Select((record, index) => (record, index)))
            for (var partIndex = 0; partIndex < pair.record.Parts.Count; partIndex++)
                features.Add(new PipeAlignment(pair.record.Parts[partIndex], string.Empty, 0, Path.GetFullPath(shpPath), $"{pair.record.RecordNumber}+{partIndex + 1}", dbf.Rows[pair.index]));
        var vertexCount = features.Sum(x => x.Vertices.Count);
        return new ShapefileReadResult(features, dbf.Fields, shp.Extent, prj.Wkt, prj.RecommendedEpsg, dbf.EncodingName,
            shp.Records.Count, vertexCount, features.Sum(x => Math.Max(0, x.Vertices.Count - 1)), warnings, dbf.Rows.FirstOrDefault(), prj.Summary);
    }
}
