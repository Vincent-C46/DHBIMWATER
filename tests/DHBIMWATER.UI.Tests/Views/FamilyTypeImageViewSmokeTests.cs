using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using DHBIMWATER.Application.Interfaces;
using DHBIMWATER.Application.Interfaces.Families;
using DHBIMWATER.Application.UseCases.Families;
using DHBIMWATER.Core.Families;
using DHBIMWATER.UI.ViewModels.Utilities;
using DHBIMWATER.UI.Views.Utilities;
using Xunit;

namespace DHBIMWATER.UI.Tests.Views;

/// <summary>
/// 유형 이미지 일괄 지정 창(docs/309) XAML 로드 스모크 테스트 — 셀 템플릿 안 StaticResource까지 펼쳐 본다.
/// <para>환경 변수 <c>DHBIM_RENDER_DIR</c>가 있으면 그 폴더에 창 캡처 PNG를 남긴다(잘림 육안 확인용).</para>
/// </summary>
public sealed class FamilyTypeImageViewSmokeTests
{
    [Fact]
    public void View_LoadsAndLaysOutRows()
    {
        var root = Path.Combine(Path.GetTempPath(), "dhbim-typeimage-view-" + Guid.NewGuid().ToString("N"));
        try
        {
            var error = RunOnStaThread(() =>
            {
                if (System.Windows.Application.Current == null) _ = new System.Windows.Application();

                var families = Directory.CreateDirectory(Path.Combine(root, "fam")).FullName;
                var images = Directory.CreateDirectory(Path.Combine(root, "img")).FullName;
                foreach (var name in new[] { "제수밸브_DN300.rfa", "아주_긴_이름의_버터플라이밸브_플랜지형_PN16_DN1200_수정본.rfa", "이음관.rfa" })
                    File.WriteAllBytes(Path.Combine(families, name), []);
                WritePng(Path.Combine(images, "gate.png"));

                var store = new StubStore(families, new TypeImageMapping(images, new Dictionary<string, string> { ["제수밸브_DN300.rfa"] = "gate.png" }));
                var vm = new FamilyTypeImageViewModel(new ApplyFamilyTypeImagesUseCase(new StubFactory(), new StubRepo()), store, new StubDialog());
                vm.LoadFamilyFolder(families);
                vm.Rows[0].SetStatus("패밀리 유형이 없습니다. 패밀리 유형 대화상자에서 유형을 하나 만든 뒤 다시 실행하세요. 긴 실패 사유 말줄임 확인용 문장.", isError: true);
                vm.Rows[1].SetStatus("완료 · 유형 12개", isError: false);

                var view = new FamilyTypeImageView(vm)
                {
                    Left = -20000, Top = -20000, ShowInTaskbar = false, ShowActivated = false,
                };
                view.Show();
                view.UpdateLayout();

                var outDir = Environment.GetEnvironmentVariable("DHBIM_RENDER_DIR");
                if (!string.IsNullOrEmpty(outDir))
                {
                    var bitmap = new RenderTargetBitmap((int)view.ActualWidth, (int)view.ActualHeight, 96, 96, PixelFormats.Pbgra32);
                    bitmap.Render(view);
                    var encoder = new PngBitmapEncoder();
                    encoder.Frames.Add(BitmapFrame.Create(bitmap));
                    using var stream = File.Create(Path.Combine(outDir, "FamilyTypeImageView.png"));
                    encoder.Save(stream);
                }
                view.Close();
            });

            Assert.Null(error);
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch (IOException) { }
        }
    }

    private static void WritePng(string path)
    {
        var pixels = new byte[32 * 32 * 4];
        for (var i = 0; i < pixels.Length; i += 4) { pixels[i] = 200; pixels[i + 1] = 120; pixels[i + 2] = 40; pixels[i + 3] = 255; }
        var source = BitmapSource.Create(32, 32, 96, 96, PixelFormats.Bgra32, null, pixels, 32 * 4);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(source));
        using var stream = File.Create(path);
        encoder.Save(stream);
    }

    private static Exception? RunOnStaThread(Action action)
    {
        Exception? captured = null;
        var thread = new Thread(() =>
        {
            try { action(); }
            catch (Exception ex) { captured = ex; }
            finally { Dispatcher.CurrentDispatcher.InvokeShutdown(); }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.IsBackground = true;
        thread.Start();
        if (!thread.Join(TimeSpan.FromSeconds(30))) return new TimeoutException("XAML 로드가 30초 안에 끝나지 않았습니다.");
        return captured;
    }

    private sealed class StubStore(string folder, TypeImageMapping mapping) : ITypeImageMappingStore
    {
        public TypeImageMapping Load(string familyFolder) => familyFolder == folder ? mapping : TypeImageMapping.Empty;
        public void Save(string familyFolder, TypeImageMapping mapping) { }
    }

    private sealed class StubFactory : IFamilyDocumentSessionFactory
    {
        public IFamilyDocumentSession Open(string rfaPath) => throw new NotSupportedException();
        public string? GetOlderVersion(string rfaPath) => null;
    }

    private sealed class StubRepo : IFamilyTypeImageRepo
    {
        public int Apply(IFamilyDocumentSession session, string imagePath) => 0;
    }

    private sealed class StubDialog : IDialogService
    {
        public void Info(string title, string message) { }
        public void Warn(string title, string message) { }
        public bool Confirm(string title, string message) => true;
    }
}
