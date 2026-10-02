using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Events;

namespace DHBIMWATER.Infrastructure.Repositories.Revit.Modeling;

/// <summary>
/// 패밀리 문서(EditFamily)에서 읽은 값과 유형 조회 사전을 <b>문서 단위</b>로 유지하는 공유 캐시(docs/258 § A·C).
/// <para>종전에는 <see cref="RevitElementTypeQueryRepo"/>의 인스턴스 필드였고 repo가 Transient라, PipeLayout 창을 열 때마다
/// 패밀리 9~12개를 다시 열었다. 파라미터 집합은 패밀리를 다시 로드하기 전에는 바뀌지 않으므로 문서가 열려 있는 동안 유지한다.</para>
/// <para>무효화: 패밀리 재로드(<see cref="OnFamilyLoadedIntoDocument"/>) → 그 패밀리 항목과 그 문서의 유형 사전,
/// 문서 닫기(<see cref="OnDocumentClosing"/>) → 그 문서 항목 전부, <see cref="Clear"/> → 전부(창의 명시적 새로고침).</para>
/// <para>Revit API는 한 스레드에서 돌지만, UI 쪽 호출 순서를 장담할 수 없어 잠금을 둔다. 비용은 무시할 만하다.</para>
/// </summary>
public static class FamilyParameterCache
{
    private static readonly object Gate = new();
    // 키 = "문서Hash|패밀리명" — 종전 인스턴스 캐시와 같은 키다.
    private static readonly Dictionary<string, IReadOnlyList<string>> Names = new();
    private static readonly Dictionary<string, IReadOnlyList<string>> NumericNames = new();
    private static readonly Dictionary<string, int> PointCounts = new();
    // 문서Hash → ("패밀리 : 유형" → ElementId). 조회마다 FamilySymbol 전체를 훑지 않게 한다(§ C).
    private static readonly Dictionary<int, Dictionary<string, ElementId>> SymbolIndex = new();

    internal static string Key(Document doc, string familyName) => $"{doc.GetHashCode()}|{familyName}";

    internal static bool TryGetNames(string key, out IReadOnlyList<string> names)
    {
        lock (Gate) return Names.TryGetValue(key, out names!);
    }

    internal static bool TryGetNumericNames(string key, out IReadOnlyList<string> names)
    {
        lock (Gate) return NumericNames.TryGetValue(key, out names!);
    }

    internal static bool TryGetPointCount(string key, out int count)
    {
        lock (Gate) return PointCounts.TryGetValue(key, out count);
    }

    internal static void SetFamilyDocument(string key, IReadOnlyList<string> names, IReadOnlyList<string> numericNames, int pointCount)
    {
        lock (Gate)
        {
            Names[key] = names;
            NumericNames[key] = numericNames;
            PointCounts[key] = pointCount;
        }
    }

    internal static void SetPointCount(string key, int count)
    {
        lock (Gate) PointCounts[key] = count;
    }

    /// <summary>"패밀리 : 유형"의 FamilySymbol. 사전에 없거나 요소가 무효면 사전을 한 번 다시 만든다 —
    /// 새로 복제·로드된 유형과 지워진 유형도 맞게 찾는다. 그래도 없으면 null.</summary>
    internal static FamilySymbol? FindSymbol(Document doc, string familyName, string typeName)
    {
        var key = $"{familyName} : {typeName}";
        var docKey = doc.GetHashCode();
        lock (Gate)
        {
            if (SymbolIndex.TryGetValue(docKey, out var index) && index.TryGetValue(key, out var cachedId)
                && doc.GetElement(cachedId) is FamilySymbol { IsValidObject: true } cached
                && cached.FamilyName == familyName && cached.Name == typeName)
                return cached;

            index = new Dictionary<string, ElementId>(StringComparer.Ordinal);
            foreach (var symbol in new FilteredElementCollector(doc).OfClass(typeof(FamilySymbol)).Cast<FamilySymbol>())
                index.TryAdd($"{symbol.FamilyName} : {symbol.Name}", symbol.Id);
            SymbolIndex[docKey] = index;
            return index.TryGetValue(key, out var id) ? doc.GetElement(id) as FamilySymbol : null;
        }
    }

    /// <summary>창의 명시적 새로고침(<see cref="RevitElementTypeQueryRepo.InvalidateFamilyCaches"/>). 전부 비운다.</summary>
    public static void Clear()
    {
        lock (Gate)
        {
            Names.Clear();
            NumericNames.Clear();
            PointCounts.Clear();
            SymbolIndex.Clear();
        }
    }

    /// <summary>패밀리를 로드·재로드하면 그 패밀리의 파라미터 집합이 바뀌었을 수 있다 — 그 항목과 문서의 유형 사전을 지운다.</summary>
    public static void OnFamilyLoadedIntoDocument(object? sender, FamilyLoadedIntoDocumentEventArgs e)
    {
        if (e.Document is not { } doc) return;
        var key = Key(doc, e.FamilyName);
        lock (Gate)
        {
            Names.Remove(key);
            NumericNames.Remove(key);
            PointCounts.Remove(key);
            SymbolIndex.Remove(doc.GetHashCode());
        }
    }

    /// <summary>문서를 닫으면 그 문서 항목을 모두 지운다 — 해시가 다른 문서에 다시 쓰여도 낡은 값이 남지 않게.</summary>
    public static void OnDocumentClosing(object? sender, DocumentClosingEventArgs e)
    {
        if (e.Document is not { } doc) return;
        var docKey = doc.GetHashCode();
        var prefix = $"{docKey}|";
        lock (Gate)
        {
            foreach (var key in Names.Keys.Where(x => x.StartsWith(prefix, StringComparison.Ordinal)).ToList()) Names.Remove(key);
            foreach (var key in NumericNames.Keys.Where(x => x.StartsWith(prefix, StringComparison.Ordinal)).ToList()) NumericNames.Remove(key);
            foreach (var key in PointCounts.Keys.Where(x => x.StartsWith(prefix, StringComparison.Ordinal)).ToList()) PointCounts.Remove(key);
            SymbolIndex.Remove(docKey);
        }
    }
}
