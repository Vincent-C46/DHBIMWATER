using System.Collections.ObjectModel;
using System.IO;
using System.Text.RegularExpressions;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using DHBIMWATER.Application.Interfaces;
using DHBIMWATER.Application.Interfaces.Families;
using DHBIMWATER.Application.UseCases.Families;
using DHBIMWATER.Core.Families;
using DHBIMWATER.UI.Base;
using DHBIMWATER.UI.Commands;

namespace DHBIMWATER.UI.ViewModels.Utilities;

/// <summary>
/// 패밀리 유형 이미지 일괄 지정 창(docs/309 § 3).
/// 패밀리 폴더 바로 아래 .rfa 목록 → 행마다 이미지 매핑 → 매핑 저장(JSON) → 체크한 행 실행.
/// <para>모달 창 + 외부 명령 실행 중 → API 컨텍스트 유효. 실행은 동기, ExternalEvent 안 씀.</para>
/// </summary>
public sealed class FamilyTypeImageViewModel : ViewModelBase
{
    // Revit 저장 백업 "이름.0001.rfa"는 목록에서 뺀다.
    private static readonly Regex BackupPattern = new(@"\.\d{4}\.rfa$", RegexOptions.IgnoreCase);

    private readonly ApplyFamilyTypeImagesUseCase _useCase;
    private readonly ITypeImageMappingStore _store;
    private readonly IDialogService _dialog;
    private string _familyFolder = "";
    private string _imageFolder = "";
    private string _summary = "패밀리 폴더를 고르세요.";

    public FamilyTypeImageViewModel(ApplyFamilyTypeImagesUseCase useCase, ITypeImageMappingStore store, IDialogService dialog)
    {
        _useCase = useCase;
        _store = store;
        _dialog = dialog;
        PickFamilyFolderCommand = new RelayCommand(_ => PickFamilyFolder());
        PickImageFolderCommand = new RelayCommand(_ => PickImageFolder(), _ => HasFamilyFolder);
        BrowseImageCommand = new RelayCommand(p => BrowseImage(p as FamilyTypeImageRow));
        CheckAllCommand = new RelayCommand(_ => SetAll(true), _ => Rows.Count > 0);
        UncheckAllCommand = new RelayCommand(_ => SetAll(false), _ => Rows.Count > 0);
        SaveMappingCommand = new RelayCommand(_ => SaveMapping(showMessage: true), _ => HasFamilyFolder);
        RunCommand = new RelayCommand(_ => Run(), _ => Rows.Any(r => r.IsChecked));
    }

    public ObservableCollection<FamilyTypeImageRow> Rows { get; } = new();
    /// <summary>이미지 폴더 안 이미지 상대경로(하위 폴더 포함). 행 콤보 목록.</summary>
    public ObservableCollection<string> ImageChoices { get; } = new();

    public string FamilyFolder { get => _familyFolder; private set { if (SetProperty(ref _familyFolder, value)) OnPropertyChanged(nameof(HasFamilyFolder)); } }
    public string ImageFolder { get => _imageFolder; private set => SetProperty(ref _imageFolder, value); }
    public bool HasFamilyFolder => !string.IsNullOrEmpty(FamilyFolder);
    public string Summary { get => _summary; private set => SetProperty(ref _summary, value); }

    public ICommand PickFamilyFolderCommand { get; }
    public ICommand PickImageFolderCommand { get; }
    public ICommand BrowseImageCommand { get; }
    public ICommand CheckAllCommand { get; }
    public ICommand UncheckAllCommand { get; }
    public ICommand SaveMappingCommand { get; }
    public ICommand RunCommand { get; }

    /// <summary>폴더 열기 — 테스트·창 공용. 저장 매핑이 있으면 이미지 폴더·행 매핑 복원.</summary>
    public void LoadFamilyFolder(string folder)
    {
        FamilyFolder = folder;
        var mapping = _store.Load(folder);
        ImageFolder = mapping.ImageFolder;
        RefreshImageChoices();

        Rows.Clear();
        // § 6 Q3 — 하위 폴더 제외.
        foreach (var path in Directory.GetFiles(folder, "*.rfa", SearchOption.TopDirectoryOnly)
                     .Where(p => !BackupPattern.IsMatch(p))
                     .OrderBy(p => p, StringComparer.OrdinalIgnoreCase))
        {
            var name = Path.GetFileName(path);
            mapping.Images.TryGetValue(name, out var stored);
            var row = new FamilyTypeImageRow(path, name, () => ImageFolder) { ImageStored = stored ?? "" };
            row.IsChecked = row.ResolvedImagePath is not null;
            Rows.Add(row);
        }
        Summary = Rows.Count == 0 ? "이 폴더 바로 아래에 .rfa 파일이 없습니다." : $"패밀리 {Rows.Count}개 · 매핑 {Rows.Count(r => r.ResolvedImagePath is not null)}개";
    }

    public void SetImageFolder(string folder)
    {
        // 기존 상대경로 매핑은 새 폴더 기준으로 다시 해석된다 — 절대경로로 바꿔 두지 않음(§ 6 Q5 상대경로 저장).
        ImageFolder = folder;
        RefreshImageChoices();
        foreach (var row in Rows) row.RefreshImage();
    }

    private void PickFamilyFolder()
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog { Title = "패밀리(.rfa) 폴더 선택" };
        if (HasFamilyFolder) dialog.InitialDirectory = FamilyFolder;
        if (dialog.ShowDialog() == true) LoadFamilyFolder(dialog.FolderName);
    }

    private void PickImageFolder()
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog { Title = "이미지 폴더 선택" };
        if (!string.IsNullOrEmpty(ImageFolder) && Directory.Exists(ImageFolder)) dialog.InitialDirectory = ImageFolder;
        if (dialog.ShowDialog() == true) SetImageFolder(dialog.FolderName);
    }

    private void BrowseImage(FamilyTypeImageRow? row)
    {
        if (row is null) return;
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = $"{row.FileName} 유형 이미지",
            Filter = "이미지 (*.png;*.jpg;*.jpeg;*.bmp)|*.png;*.jpg;*.jpeg;*.bmp",
        };
        if (!string.IsNullOrEmpty(ImageFolder) && Directory.Exists(ImageFolder)) dialog.InitialDirectory = ImageFolder;
        if (dialog.ShowDialog() != true) return;
        row.ImageStored = TypeImagePaths.ToStored(ImageFolder, dialog.FileName);
        row.IsChecked = true;
    }

    private void RefreshImageChoices()
    {
        ImageChoices.Clear();
        if (string.IsNullOrEmpty(ImageFolder) || !Directory.Exists(ImageFolder)) return;
        foreach (var path in Directory.GetFiles(ImageFolder, "*.*", SearchOption.AllDirectories)
                     .Where(TypeImagePaths.IsImageFile)
                     .OrderBy(p => p, StringComparer.OrdinalIgnoreCase))
            ImageChoices.Add(TypeImagePaths.ToStored(ImageFolder, path));
    }

    private void SetAll(bool value)
    {
        foreach (var row in Rows) row.IsChecked = value;
    }

    private void SaveMapping(bool showMessage)
    {
        var images = Rows.Where(r => !string.IsNullOrWhiteSpace(r.ImageStored))
            .ToDictionary(r => r.FileName, r => r.ImageStored.Trim(), StringComparer.OrdinalIgnoreCase);
        _store.Save(FamilyFolder, new TypeImageMapping(ImageFolder, images));
        if (showMessage) _dialog.Info("유형 이미지", $"매핑 {images.Count}개를 저장했습니다.");
    }

    private void Run()
    {
        var checkedRows = Rows.Where(r => r.IsChecked).ToList();
        var missing = checkedRows.Where(r => r.ResolvedImagePath is null || !File.Exists(r.ResolvedImagePath)).ToList();
        foreach (var row in missing) row.SetStatus("이미지 미지정 또는 파일 없음", isError: true);
        var targets = checkedRows.Except(missing).ToList();
        if (targets.Count == 0)
        {
            _dialog.Warn("유형 이미지", "실행할 행이 없습니다. 체크한 행에 이미지를 지정하세요.");
            return;
        }

        // § 4-4 — 원본 덮어쓰기 · 버전 업그레이드는 되돌릴 수 없으므로 실행 전 확인.
        var older = _useCase.CountOlderVersion(targets.Select(r => r.FamilyPath));
        var message = $"패밀리 {targets.Count}개의 원본 파일을 덮어씁니다(백업 없음)."
            + (older > 0 ? $"\n구버전 파일 {older}개는 현재 Revit 버전으로 올라가며 되돌릴 수 없습니다." : "")
            + (missing.Count > 0 ? $"\n이미지 없는 {missing.Count}개는 건너뜁니다." : "")
            + "\n\n진행할까요?";
        if (!_dialog.Confirm("유형 이미지 일괄 지정", message)) return;

        SaveMapping(showMessage: false);
        foreach (var row in targets) row.SetStatus("대기", isError: false);

        var byPath = targets.ToDictionary(r => r.FamilyPath, StringComparer.OrdinalIgnoreCase);
        var previousCursor = Mouse.OverrideCursor;
        Mouse.OverrideCursor = Cursors.Wait;
        IReadOnlyList<FamilyTypeImageResult> results;
        try
        {
            results = _useCase.Execute(
                targets.Select(r => (r.FamilyPath, r.ResolvedImagePath!)).ToList(),
                result => byPath[result.FamilyPath].SetStatus(result.Message, !result.Succeeded));
        }
        finally
        {
            Mouse.OverrideCursor = previousCursor;
        }

        var ok = results.Count(r => r.Succeeded);
        Summary = $"완료 {ok}개 · 실패 {results.Count - ok}개" + (missing.Count > 0 ? $" · 건너뜀 {missing.Count}개" : "");
        _dialog.Info("유형 이미지 일괄 지정", Summary);
    }
}

/// <summary>표 한 행 = 패밀리 파일 하나.</summary>
public sealed class FamilyTypeImageRow : ViewModelBase
{
    private readonly Func<string> _imageFolder;
    private bool _isChecked;
    private string _imageStored = "";
    private ImageSource? _thumbnail;
    private string _status = "";
    private bool _isError;

    public FamilyTypeImageRow(string familyPath, string fileName, Func<string> imageFolder)
    {
        FamilyPath = familyPath;
        FileName = fileName;
        _imageFolder = imageFolder;
    }

    public string FamilyPath { get; }
    public string FileName { get; }
    public bool IsChecked { get => _isChecked; set => SetProperty(ref _isChecked, value); }

    /// <summary>저장값(이미지 폴더 기준 상대경로 또는 절대경로).</summary>
    public string ImageStored
    {
        get => _imageStored;
        set { if (SetProperty(ref _imageStored, value ?? "")) RefreshImage(); }
    }

    public string? ResolvedImagePath => TypeImagePaths.Resolve(_imageFolder(), ImageStored);
    public ImageSource? Thumbnail { get => _thumbnail; private set => SetProperty(ref _thumbnail, value); }
    public string Status { get => _status; private set => SetProperty(ref _status, value); }
    public bool IsError { get => _isError; private set => SetProperty(ref _isError, value); }

    public void SetStatus(string status, bool isError)
    {
        Status = status;
        IsError = isError;
    }

    public void RefreshImage()
    {
        OnPropertyChanged(nameof(ResolvedImagePath));
        Thumbnail = LoadThumbnail(ResolvedImagePath);
    }

    private static ImageSource? LoadThumbnail(string? path)
    {
        if (path is null || !File.Exists(path)) return null;
        try
        {
            // 바이트로 읽어 디코딩 → 디코딩 실패해도 파일 잠금 안 남음(UriSource는 실패 시 잠금 남음). 썸네일 크기로만 디코딩.
            using var stream = new MemoryStream(File.ReadAllBytes(path));
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.DecodePixelWidth = 64;
            bitmap.StreamSource = stream;
            bitmap.EndInit();
            bitmap.Freeze();
            return bitmap;
        }
        catch (Exception ex) when (ex is IOException or NotSupportedException or UnauthorizedAccessException or FormatException or InvalidOperationException)
        {
            return null;
        }
    }
}
