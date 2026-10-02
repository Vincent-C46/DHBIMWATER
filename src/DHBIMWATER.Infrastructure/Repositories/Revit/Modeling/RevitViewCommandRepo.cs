using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using DHBIMWATER.Application.Interfaces;
using DHBIMWATER.Core.Geometry;
using DHBIMWATER.Core.Structures;
using System.Diagnostics;
using System.Windows.Controls;
using UC = DHBIMWATER.Infrastructure.Converters.RevitUnitConverter;

namespace DHBIMWATER.Infrastructure.Repositories.Revit.Modeling
{
    public class RevitViewCommandRepo : IViewCommandRepo
    {
        private readonly Func<Document?> _doc;

        public RevitViewCommandRepo(Func<Document?> doc)
        {
            _doc = doc;
        }

        public int CreateSectionView(SectionViewDefinition sectionViewDef)
        {
            var doc = _doc();
            if (doc == null) return 0;

            var vft = new FilteredElementCollector(doc)
                    .OfClass(typeof(ViewFamilyType))
                    .Cast<ViewFamilyType>()
                    .FirstOrDefault(vft => vft.ViewFamily == ViewFamily.Section);

            // 기존 SectionView 있으면 삭제
            var existingSectionView = new FilteredElementCollector(doc)
                              .OfClass(typeof(ViewSection))
                              .WhereElementIsNotElementType()
                              .Cast<ViewSection>()
                              .FirstOrDefault(vs => vs.Name == sectionViewDef.Name);

            if(existingSectionView!=null)
            {
                using var subTx = new SubTransaction(doc);
                subTx.Start();
                doc.Delete(existingSectionView.Id);
                subTx.Commit();
            }
            doc.Regenerate();

            Transform transform = new Transform(Transform.Identity);
            transform.Origin = XYZ.Zero;
            transform.BasisX = new XYZ(sectionViewDef.BasisX.X, sectionViewDef.BasisX.Y, sectionViewDef.BasisX.Z).Normalize();
            transform.BasisZ = new XYZ(sectionViewDef.BasisZ.X, sectionViewDef.BasisZ.Y, sectionViewDef.BasisZ.Z).Normalize();
            transform.BasisY = transform.BasisX.CrossProduct(transform.BasisZ).Normalize();

            var boundingBox = new BoundingBoxXYZ();
            boundingBox.Transform = transform;

            var boundingBoxInversedMin = transform.Inverse.OfPoint(
                new XYZ(UC.MmToFt(sectionViewDef.Min.X),
                UC.MmToFt(sectionViewDef.Min.Y),
                UC.MmToFt(sectionViewDef.Min.Z)));
            var boundingBoxInversedMax = transform.Inverse.OfPoint(
                new XYZ(UC.MmToFt(sectionViewDef.Max.X),
                UC.MmToFt(sectionViewDef.Max.Y),
                UC.MmToFt(sectionViewDef.Max.Z)));

            boundingBox.Min = new XYZ(boundingBoxInversedMin.X < boundingBoxInversedMax.X ? boundingBoxInversedMin.X : boundingBoxInversedMax.X,
                                      boundingBoxInversedMin.Y < boundingBoxInversedMax.Y ? boundingBoxInversedMin.Y : boundingBoxInversedMax.Y,
                                      boundingBoxInversedMin.Z < boundingBoxInversedMax.Z ? boundingBoxInversedMin.Z : boundingBoxInversedMax.Z);
            boundingBox.Max = new XYZ(boundingBoxInversedMin.X > boundingBoxInversedMax.X ? boundingBoxInversedMin.X : boundingBoxInversedMax.X,
                                      boundingBoxInversedMin.Y > boundingBoxInversedMax.Y ? boundingBoxInversedMin.Y : boundingBoxInversedMax.Y,
                                      boundingBoxInversedMin.Z > boundingBoxInversedMax.Z ? boundingBoxInversedMin.Z : boundingBoxInversedMax.Z);

            if (boundingBox.Min.X - boundingBox.Max.X == 0 || boundingBox.Min.Y - boundingBox.Max.Y == 0 || boundingBox.Min.Z - boundingBox.Max.Z == 0)
            {
                TaskDialog.Show("Error", "Min, Max 확인");
            }

            var viewSection = ViewSection.CreateSection(doc, vft.Id, boundingBox);
            viewSection.Name = sectionViewDef.Name; 

            viewSection.LookupParameter("DH_뷰 카테고리")?.Set("모델링");
            viewSection.LookupParameter("DH_뷰 타입")?.Set("단면도");

            return (int)viewSection.Id.Value;
        }

        /// <summary>절단점·화면 오른쪽으로 단면도를 만든다(docs/246).
        /// <para>Revit 규약(ViewSection.CreateSection): BasisZ = 바라보는 방향, BasisY = 위, (right, up, view)는 왼손 좌표계.
        /// 절단면은 로컬 Min.Z에 놓이고 far clip 거리는 Max.Z - Min.Z 이다. 그래서 Min.Z = 0(절단점), Max.Z = 깊이로 둔다.</para>
        /// 트랜잭션 도중이므로 대화상자를 띄우지 않고 예외로 알린다.</summary>
        public int CreateSectionView(SectionCutDefinition cut)
        {
            var doc = _doc();
            if (doc == null) return 0;

            if (cut.HalfWidth <= 0 || cut.ZMax <= cut.ZMin || cut.FarClipDepth <= 0)
                throw new ArgumentException($"단면도 '{cut.Name}'의 범위가 올바르지 않습니다(폭·높이·깊이는 0보다 커야 함).");

            var vft = new FilteredElementCollector(doc)
                    .OfClass(typeof(ViewFamilyType))
                    .Cast<ViewFamilyType>()
                    .FirstOrDefault(v => v.ViewFamily == ViewFamily.Section)
                ?? throw new InvalidOperationException("단면도 뷰 유형(Section)이 프로젝트에 없습니다.");

            // 기존 같은 이름 단면도 삭제 — 재실행 시 지우고 다시 만든다.
            var existing = new FilteredElementCollector(doc)
                .OfClass(typeof(ViewSection))
                .WhereElementIsNotElementType()
                .Cast<ViewSection>()
                .FirstOrDefault(vs => vs.Name == cut.Name);
            if (existing != null)
            {
                using var subTx = new SubTransaction(doc);
                subTx.Start();
                doc.Delete(existing.Id);
                subTx.Commit();
                doc.Regenerate();
            }

            var right = new XYZ(cut.Right.X, cut.Right.Y, 0).Normalize();
            var up = XYZ.BasisZ;
            var transform = new Transform(Transform.Identity);
            transform.Origin = new XYZ(UC.MmToFt(cut.CutOrigin.X), UC.MmToFt(cut.CutOrigin.Y), 0);
            transform.BasisX = right;
            transform.BasisY = up;
            transform.BasisZ = up.CrossProduct(right); // 바라보는 방향 — A-A: +Z × +X = +Y, B-B: +Z × -Y = +X

            // 원점 Z=0, BasisY=+Z 이므로 로컬 y = 월드 표고.
            var box = new BoundingBoxXYZ
            {
                Transform = transform,
                Min = new XYZ(-UC.MmToFt(cut.HalfWidth), UC.MmToFt(cut.ZMin), 0),   // Min.Z = 절단면(기준점)
                Max = new XYZ(UC.MmToFt(cut.HalfWidth), UC.MmToFt(cut.ZMax), UC.MmToFt(cut.FarClipDepth))
            };

            var viewSection = ViewSection.CreateSection(doc, vft.Id, box);
            viewSection.Name = cut.Name;

            viewSection.LookupParameter("DH_뷰 카테고리")?.Set("모델링");
            viewSection.LookupParameter("DH_뷰 타입")?.Set("단면도");

            return (int)viewSection.Id.Value;
        }
        //public int UpdateSectionView(SectionViewDefinition sectionViewDef)
        //{
        //    var doc = _doc();
        //    if (doc == null) return 0;

        //    var existingSectionView = new FilteredElementCollector(doc)
        //                                  .OfClass(typeof(ViewSection))
        //                                  .WhereElementIsNotElementType()
        //                                  .Cast<ViewSection>()
        //                                  .FirstOrDefault(vs => vs.Name == sectionViewDef.Name);

        //    Transform transform = new Transform(Transform.Identity);
        //    transform.Origin = XYZ.Zero;
        //    transform.BasisX = new XYZ(sectionViewDef.BasisX.X, sectionViewDef.BasisX.Y, sectionViewDef.BasisX.Z).Normalize();
        //    transform.BasisZ = new XYZ(sectionViewDef.BasisZ.X, sectionViewDef.BasisZ.Y, sectionViewDef.BasisZ.Z).Normalize();
        //    transform.BasisY = transform.BasisX.CrossProduct(transform.BasisZ).Normalize();

        //    var boundingBox = new BoundingBoxXYZ();
        //    boundingBox.Transform = transform;

        //    var boundingBoxInversedMin = transform.Inverse.OfPoint(
        //        new XYZ(UC.MmToFt(sectionViewDef.Min.X),
        //        UC.MmToFt(sectionViewDef.Min.Y),
        //        UC.MmToFt(sectionViewDef.Min.Z)));
        //    var boundingBoxInversedMax = transform.Inverse.OfPoint(
        //        new XYZ(UC.MmToFt(sectionViewDef.Max.X),
        //        UC.MmToFt(sectionViewDef.Max.Y),
        //        UC.MmToFt(sectionViewDef.Max.Z)));

        //    boundingBox.Min = new XYZ(boundingBoxInversedMin.X < boundingBoxInversedMax.X ? boundingBoxInversedMin.X : boundingBoxInversedMax.X,
        //                              boundingBoxInversedMin.Y < boundingBoxInversedMax.Y ? boundingBoxInversedMin.Y : boundingBoxInversedMax.Y,
        //                              boundingBoxInversedMin.Z < boundingBoxInversedMax.Z ? boundingBoxInversedMin.Z : boundingBoxInversedMax.Z);
        //    boundingBox.Max = new XYZ(boundingBoxInversedMin.X > boundingBoxInversedMax.X ? boundingBoxInversedMin.X : boundingBoxInversedMax.X,
        //                              boundingBoxInversedMin.Y > boundingBoxInversedMax.Y ? boundingBoxInversedMin.Y : boundingBoxInversedMax.Y,
        //                              boundingBoxInversedMin.Z > boundingBoxInversedMax.Z ? boundingBoxInversedMin.Z : boundingBoxInversedMax.Z);

        //    existingSectionView.CropBox = boundingBox;

        //    return (int)existingSectionView.Id.Value;
        //}
    }
}
