using Autodesk.Revit.ApplicationServices;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using DHBIMWATER.Core.Parameters;
using DHBIMWATER.Infrastructure.Converters;
using DHBIMWATER.Shared.Diagnostics;
using System;
using System.Collections.Generic;
using System.IO;
using System.Windows.Controls;
using App = Autodesk.Revit.ApplicationServices.Application;
using DHBIMWATER.Shared.Errors;

namespace DHBIMWATER.Infrastructure.Repositories.Revit.Modeling
{
    public class RevitSharedParameterRepository : ISharedParameterRepository
    {
        private readonly Func<Document?> _doc;


        public RevitSharedParameterRepository(Func<Document?> doc)
        {
            _doc = doc;
        }

        public void EnsureParameters(IReadOnlyList<SharedParameterDefinition> definitions,
            IReadOnlyList<int>? extraCategoryIds = null)
        {
            if (definitions == null || definitions.Count == 0) return;

            Document? doc = _doc();
            if (doc == null) return;
            var app = doc.Application;

            var version = app.VersionNumber;
            string sharedParameterFileName = "DHBIMWATER_sharedParameters.txt";
            string sharedParameterFilePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), $@"Autodesk\Revit\Addins\{version}\{sharedParameterFileName}");

            string previousFilePath = app.SharedParametersFilename;

            if (definitions.Count == 0) return;

            // GUID 하드코딩 - 변경 금지 ❌
            var guidDict = new Dictionary<string, Guid>()
                                {
                                    { "DH_Addin",       new Guid("f0ff9795-a26f-4a2f-869c-532d2c418fac") },
                                    { "DH_ElementCode", new Guid("280f0a33-1456-4c43-9f01-ae6acf80769b") },
                                    { "DH_Class",       new Guid("5b93b43c-abd7-435b-81b1-6b7ff51419c8") },
                                    { "DH_Category",    new Guid("7e539f5b-80e4-4dd9-a7e1-ac070dbcaa24") },
                                    { "DH_Zone",        new Guid("b60a47f3-3ede-45db-8491-b8f70de329a4") },
                                    { "DH_Part",        new Guid("47c74ae0-3fc8-4a06-9c4e-80713b564b0c") },
                                    { "DH_IsExterior",  new Guid("98DE7493-0B2B-4C17-85D6-6ABA0CBF8B54") },
                                    // 형상 치수 정보
                                    { "L1",             new Guid("24ef3fdc-96cb-4e32-bb49-c8ecacd92a58") },
                                    { "W1",             new Guid("a6f6385b-2364-4288-a239-813d49e4a572") },
                                    { "L2",             new Guid("180771e8-b65a-4f97-a8e4-d00a67fa4823") },
                                    { "W2",             new Guid("f65c9ace-ffde-4b4f-a01c-b7dd303b0836") },
                                    { "L3",             new Guid("f074ebb2-5306-4082-888f-99d53b4f6f9e") },
                                    { "W3",             new Guid("e8fced45-637e-42bd-b9a9-e29f319e9f39") },
                                    { "H",              new Guid("10337573-871a-40b7-8a3d-4dc3637e9349") },
                                    { "ETC",            new Guid("5ef1d2b7-8766-482f-a81c-22fdcba1e72a") },
                                    // 뷰 관리용
                                    { "DH_뷰 카테고리",  new Guid("D65D0DD9-6E59-419C-B8A1-613C9C8E89CA") },
                                    { "DH_뷰 타입",      new Guid("D1A2EC3E-16A2-4E42-AC1B-7897E1DC43BA") },
                                    //{ "DH_RowNum",    new Guid("98122773-6f3c-49dc-a817-5bfb065d94a1") },
                                    //{ "DH_ColNum",    new Guid("a207e5bc-87dd-4062-973f-149777f98762") },
                                };

            try
            {
                string header = "*META\tVERSION\tMINVERSION\r\nMETA\t2\t1\r\n*GROUP\tID\tNAME\r\n*PARAM\tGUID\tNAME\tDATATYPE\tDATACATEGORY\tGROUP\tVISIBLE\tDESCRIPTION\tUSERMODIFIABLE\tHIDEWHENNOVALUE\r\n";
                // 애드인이 ProgramData로 설치돼 사용자 프로필에 Addins\{version} 폴더가 없으면 WriteAllText가 던졌다(docs/207 § E).
                Directory.CreateDirectory(Path.GetDirectoryName(sharedParameterFilePath)!);
                File.WriteAllText(sharedParameterFilePath, header); // 생성 or 덮어쓰기

                app.SharedParametersFilename = sharedParameterFilePath; // 경로 지정
                DefinitionFile defFile = app.OpenSharedParameterFile()
                    ?? throw new DhbimwaterException(ErrorCodes.SharedParameterFileFailed, $"공유 매개변수 파일을 열 수 없습니다: {sharedParameterFilePath}");
                BindingMap bindingMap = doc.ParameterBindings;

                // 호출부가 실측해 넘긴 추가 카테고리. 정의마다 같으므로 루프 밖에서 한 번만 만든다.
                var extraCategories = ResolveExtraCategories(doc, extraCategoryIds);

                foreach (var def in definitions)
                {
                    #region 공유 매개변수 생성
                    DefinitionGroups? groups = defFile.Groups;
                    var groupNames = groups.Select(g => g.Name);
                    DefinitionGroup? group;

                    if (!groupNames.Contains(def.GroupName))
                    {
                        group = groups.Create(def.GroupName);   // 그룹이 없으면 새로 생성
                    }
                    else
                        group = groups.get_Item(def.GroupName); // 그룹이 이미 존재하면 가져오기

                    ForgeTypeId specType = RevitParameterMapper.ToSpecTypeId(def.SpecType);
                    ForgeTypeId groupType = RevitParameterMapper.ToGroupTypeId(def.GroupType);

                    ExternalDefinitionCreationOptions options = new ExternalDefinitionCreationOptions(def.Name, specType);

                    options.GUID = def.Guid ?? (guidDict.TryGetValue(def.Name, out var guid) ? guid : Guid.NewGuid());
                    options.UserModifiable = def.UserModifiable;
                    options.Visible = true;

                    // 공유 매개변수 생성
                    Definition sharedDef = group.Definitions.get_Item(def.Name) ?? group.Definitions.Create(options);
                    //if (sharedDef == null) continue;
                    #endregion

                    #region 프로젝트 매개변수 생성
                    CategorySet categorySet = RevitParameterMapper.ToCategorySet(def.Categories, doc);
                    foreach (Category extra in extraCategories) categorySet.Insert(extra);
                    Binding binding = RevitParameterMapper.ToBinding(def.BindingType, categorySet, app);

                    // 동명 매개변수가 이미 다른 카테고리로 묶여 있어도 이번 카테고리를 반드시 더한다.
                    // 이름만 보고 건너뛰면 외부 애드인이 먼저 만든 동명 매개변수 탓에
                    // 일반 모델 바인딩이 빠지고 값 기록이 통째로 실패한다.
                    (Definition? boundDef, ElementBinding? boundBinding) = FindExistingBinding(doc, def.Name);

                    if (boundDef == null)
                    {
                        bindingMap.Insert(sharedDef, binding, groupType);
                        PlacementProfiler.Count("공유 매개변수 신규 바인딩", 1);   // docs/197 § A-4 (계측 전용)
                    }
                    else
                    {
                        CategorySet merged = app.Create.NewCategorySet();
                        if (boundBinding != null)
                        {
                            foreach (Category existingCategory in boundBinding.Categories)
                                merged.Insert(existingCategory);
                        }

                        bool added = false;
                        foreach (Category wanted in categorySet)
                        {
                            if (merged.Contains(wanted)) continue;
                            merged.Insert(wanted);
                            added = true;
                        }

                        // 빠진 카테고리 없으면 그대로 둔다. 괜한 ReInsert 안 한다.
                        if (added)
                        {
                            Binding mergedBinding = RevitParameterMapper.ToBinding(def.BindingType, merged, app);
                            bindingMap.ReInsert(boundDef, mergedBinding, groupType);
                            PlacementProfiler.Count("공유 매개변수 바인딩 갱신", 1);   // docs/197 § A-4 (계측 전용)
                        }
                    }
                    #endregion
                }
            }
            finally
            {
                // 기존 공유매개변수 복원
                app.SharedParametersFilename = previousFilePath;
            }
        }

        /// <summary>추가 바인딩 카테고리(BuiltInCategory 정수값)를 Revit Category로 바꾼다.
        /// 알 수 없는 값이나 매개변수를 붙일 수 없는 카테고리는 조용히 버린다 —
        /// 여기서 예외를 던지면 이미 진행 중인 배치 트랜잭션이 통째로 롤백된다(docs/141).</summary>
        private static IReadOnlyList<Category> ResolveExtraCategories(Document doc, IReadOnlyList<int>? categoryIds)
        {
            if (categoryIds is null || categoryIds.Count == 0) return Array.Empty<Category>();

            var resolved = new List<Category>();
            foreach (var id in categoryIds.Distinct())
            {
                Category? category = null;
                try { category = doc.Settings.Categories.get_Item((BuiltInCategory)id); }
                catch { /* 이 문서에 없는 카테고리. 버린다. */ }
                if (category is not null && category.AllowsBoundParameters) resolved.Add(category);
            }
            return resolved;
        }

        // 이름 같은 정의가 이미 바인딩됐으면 그 정의와 바인딩을 준다. 없으면 (null, null).
        // ReInsert는 바인딩맵에 들어 있는 그 Definition 객체를 요구한다. 새로 만든 sharedDef 아님.
        private static (Definition? Definition, ElementBinding? Binding) FindExistingBinding(Document doc, string name)
        {
            DefinitionBindingMapIterator iterator = doc.ParameterBindings.ForwardIterator();
            iterator.Reset();
            while (iterator.MoveNext())
            {
                if (iterator.Key is Definition existing && existing.Name == name)
                    return (existing, iterator.Current as ElementBinding);
            }
            return (null, null);
        }

    }
}
