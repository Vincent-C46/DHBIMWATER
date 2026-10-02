using System;
using System.Collections.Generic;
using System.Linq;

namespace DHBIMWATER.Core.Structures
{
    /// <summary>콘크리트 강도를 나눠 지정하는 부재 구분(docs/249 § A).</summary>
    public enum ConcreteMember
    {
        Wall,
        Slab,
        Foundation,
        LeanConcrete,
        Beam,
        Column,
        Stair,
    }

    /// <summary>
    /// 구조물 공용 부재별 콘크리트 설정(docs/249). 펌프장·토출조·배수지·밸브실이 같은 값을 쓰며
    /// 문서 DataStorage에 저장된다. 저장값이 없으면 <see cref="Default"/>(펌프장 기존 기본값)를 쓴다.
    /// </summary>
    public sealed record ConcreteSettings(
        ConcreteSpec Wall,
        ConcreteSpec Slab,
        ConcreteSpec Foundation,
        ConcreteSpec LeanConcrete,
        ConcreteSpec Beam,
        ConcreteSpec Column,
        ConcreteSpec Stair)
    {
        public static readonly ConcreteSpec DefaultSpec = new(25, 24, 120);
        public static readonly ConcreteSpec DefaultLeanConcreteSpec = new(25, 18, 120);

        public static ConcreteSettings Default { get; } = new(
            DefaultSpec, DefaultSpec, DefaultSpec, DefaultLeanConcreteSpec, DefaultSpec, DefaultSpec, DefaultSpec);

        /// <summary>표에 보여줄 순서.</summary>
        public static IReadOnlyList<ConcreteMember> Members { get; } = new[]
        {
            ConcreteMember.Wall, ConcreteMember.Slab, ConcreteMember.Foundation, ConcreteMember.LeanConcrete,
            ConcreteMember.Beam, ConcreteMember.Column, ConcreteMember.Stair,
        };

        public ConcreteSpec Get(ConcreteMember member) => member switch
        {
            ConcreteMember.Wall => Wall,
            ConcreteMember.Slab => Slab,
            ConcreteMember.Foundation => Foundation,
            ConcreteMember.LeanConcrete => LeanConcrete,
            ConcreteMember.Beam => Beam,
            ConcreteMember.Column => Column,
            ConcreteMember.Stair => Stair,
            _ => throw new ArgumentOutOfRangeException(nameof(member), member, null),
        };

        public ConcreteSettings With(ConcreteMember member, ConcreteSpec spec) => member switch
        {
            ConcreteMember.Wall => this with { Wall = spec },
            ConcreteMember.Slab => this with { Slab = spec },
            ConcreteMember.Foundation => this with { Foundation = spec },
            ConcreteMember.LeanConcrete => this with { LeanConcrete = spec },
            ConcreteMember.Beam => this with { Beam = spec },
            ConcreteMember.Column => this with { Column = spec },
            ConcreteMember.Stair => this with { Stair = spec },
            _ => throw new ArgumentOutOfRangeException(nameof(member), member, null),
        };

        public static string DisplayName(ConcreteMember member) => member switch
        {
            ConcreteMember.Wall => "벽체",
            ConcreteMember.Slab => "슬래브",
            ConcreteMember.Foundation => "기초",
            ConcreteMember.LeanConcrete => "버림콘크리트",
            ConcreteMember.Beam => "보(거더·헌치)",
            ConcreteMember.Column => "기둥",
            ConcreteMember.Stair => "계단",
            _ => member.ToString(),
        };

        /// <summary>
        /// 바닥(SlabDefinition.Part) 이름으로 부재를 정한다. 버림은 기초보다 먼저 본다("기초버림슬래브" 등).
        /// 덧침콘크리트·토출조 "바닥슬래브"는 슬래브로 본다(docs/249 § A).
        /// </summary>
        public static ConcreteMember ClassifySlabPart(string? part)
        {
            if (string.IsNullOrEmpty(part)) return ConcreteMember.Slab;
            if (part.Contains("버림")) return ConcreteMember.LeanConcrete;
            if (part.Contains("기초")) return ConcreteMember.Foundation;
            return ConcreteMember.Slab;
        }

        /// <summary>
        /// (두께, 콘크리트)마다 바닥·벽 유형 이름을 정한다(docs/249 § D). 기본은 "일반 - {t}mm"이고,
        /// 한 번의 생성에서 같은 두께에 서로 다른 콘크리트가 섞이면 그 두께만 "일반 - {t}mm_{강도}MPa"로 나눈다.
        /// 강도는 같고 골재·슬럼프만 다르면 이름이 겹친다 — 사용자 지정(강도만 표기)에 따른 한계로, 그 경우 같은 유형을 쓴다.
        /// </summary>
        public static IReadOnlyDictionary<(double Thickness, ConcreteSpec Concrete), string> ResolveTypeNames(
            IEnumerable<(double Thickness, ConcreteSpec Concrete)> usages)
        {
            var result = new Dictionary<(double, ConcreteSpec), string>();
            foreach (var group in usages.Distinct().GroupBy(u => u.Thickness))
            {
                var split = group.Count() > 1;
                foreach (var (thickness, concrete) in group)
                    result[(thickness, concrete)] = split
                        ? $"일반 - {thickness}mm_{concrete.CompressiveStrength}MPa"
                        : $"일반 - {thickness}mm";
            }
            return result;
        }
    }
}
