using System.IO;
using System.Text.RegularExpressions;
using Autodesk.Revit.DB;
using DHBIMWATER.Application.Interfaces.Families;
using RevitApp = Autodesk.Revit.ApplicationServices.Application;

namespace DHBIMWATER.Infrastructure.Repositories.Revit.Families;

/// <summary>
/// 백그라운드 패밀리 문서 세션(docs/309 § 6 Q1). 활성 문서와 무관하게 파일을 직접 연다.
/// <para>외부 명령 실행 중(API 컨텍스트)에만 호출 가능.</para>
/// </summary>
internal sealed class RevitFamilyDocumentSession : IFamilyDocumentSession
{
    // Revit 저장 백업: "이름.0001.rfa"
    private static readonly Regex BackupPattern = new(@"\.\d{4}\.rfa$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private Transaction? _transaction;

    public RevitFamilyDocumentSession(Document document, string path)
    {
        Document = document;
        Path = path;
    }

    internal Document Document { get; }
    public string Path { get; }

    public void Begin(string name)
    {
        _transaction = new Transaction(Document, name);
        _transaction.Start();
    }

    public void Commit()
    {
        _transaction?.Commit();
        _transaction = null;
    }

    public void Rollback()
    {
        if (_transaction?.HasStarted() == true && !_transaction.HasEnded()) _transaction.RollBack();
        _transaction = null;
    }

    public void Save()
    {
        var before = Backups().ToHashSet(StringComparer.OrdinalIgnoreCase);
        Document.Save();
        // 원본 덮어쓰기 방식(§ 6 Q2) — 이번 저장이 새로 만든 백업만 지움. 원래 있던 백업은 둠.
        foreach (var backup in Backups().Where(b => !before.Contains(b)))
        {
            try { File.Delete(backup); } catch (System.IO.IOException) { } catch (UnauthorizedAccessException) { }
        }
    }

    public void Dispose()
    {
        Rollback();
        if (Document.IsValidObject) Document.Close(false);
    }

    private IEnumerable<string> Backups()
    {
        var folder = System.IO.Path.GetDirectoryName(Path);
        if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder)) return [];
        var stem = System.IO.Path.GetFileNameWithoutExtension(Path);
        return Directory.GetFiles(folder, stem + ".*.rfa")
            .Where(f => BackupPattern.IsMatch(f) && System.IO.Path.GetFileName(f).Length == stem.Length + ".0000.rfa".Length);
    }
}

internal sealed class RevitFamilyDocumentSessionFactory : IFamilyDocumentSessionFactory
{
    private readonly Func<RevitApp> _app;

    public RevitFamilyDocumentSessionFactory(Func<RevitApp> app) => _app = app;

    public IFamilyDocumentSession Open(string rfaPath)
    {
        var document = _app().OpenDocumentFile(rfaPath)
            ?? throw new InvalidOperationException("파일을 열지 못했습니다.");
        if (!document.IsFamilyDocument)
        {
            document.Close(false);
            throw new InvalidOperationException("패밀리 문서가 아닙니다.");
        }
        return new RevitFamilyDocumentSession(document, rfaPath);
    }

    public string? GetOlderVersion(string rfaPath)
    {
        try
        {
            var format = BasicFileInfo.Extract(rfaPath).Format;
            return int.TryParse(format, out var saved) && int.TryParse(_app().VersionNumber, out var current) && saved < current
                ? format
                : null;
        }
        catch (Exception)
        {
            // 판독 실패는 확인창 숫자에서만 빠짐. 실제 열기 실패는 실행 결과에 나옴.
            return null;
        }
    }
}
