using System.Collections.Generic;
using System.Text;
using UnityEngine;

/// <summary>
/// 지층 목록을 실제 Y 경계로 푼 결과.
///
/// <see cref="StrataDefinition.thicknessWeight"/>의 합으로 맵 높이를 나눠 각 층의 두께를 정합니다.
/// 목록의 <b>첫 항목이 맨 위(하늘), 마지막이 맨 아래(최하층)</b>입니다.
///
/// 비율로 두는 이유는 맵 높이를 바꿔도 층 구성이 그대로 따라오게 하기 위함입니다 —
/// 절대 Y를 박으면 맵을 키울 때마다 여섯 층을 전부 다시 계산해야 합니다.
/// 나눠떨어지지 않는 나머지는 <b>아래층부터</b> 한 칸씩 나눠 갖습니다(깊은 층이 조금 더 두꺼워짐).
/// </summary>
public class StrataLayout
{
    /// <summary>층 하나의 Y 범위. bottomY ≤ y ≤ topY (양끝 포함)</summary>
    public readonly struct Band
    {
        public readonly StrataDefinition definition;
        public readonly int bottomY;
        public readonly int topY;

        public Band(StrataDefinition definition, int bottomY, int topY)
        {
            this.definition = definition;
            this.bottomY = bottomY;
            this.topY = topY;
        }

        public int Thickness => topY - bottomY + 1;
    }

    private readonly List<Band> bands = new List<Band>();

    /// <summary>Y → 지층 정의 조회표. 매 칸 검색을 피하려고 미리 펼쳐 둡니다.</summary>
    private readonly StrataDefinition[] byY;

    public IReadOnlyList<Band> Bands => bands;
    public int MapHeight => byY.Length;

    /// <summary>지층이 하나도 없어 조회가 전부 null인지</summary>
    public bool IsEmpty => bands.Count == 0;

    /// <summary>
    /// 지층 목록과 맵 높이로 경계를 계산합니다.
    /// 가중치가 0 이하인 항목과 null은 건너뜁니다.
    /// </summary>
    public StrataLayout(IReadOnlyList<StrataDefinition> strata, int mapHeight)
    {
        byY = new StrataDefinition[Mathf.Max(1, mapHeight)];
        if (strata == null || strata.Count == 0 || mapHeight <= 0) return;

        // 1) 유효한 층만 추리고 가중치 합을 구한다
        var valid = new List<StrataDefinition>();
        int totalWeight = 0;
        foreach (var def in strata)
        {
            if (def == null || def.thicknessWeight <= 0) continue;
            valid.Add(def);
            totalWeight += def.thicknessWeight;
        }
        if (valid.Count == 0 || totalWeight <= 0) return;

        // 2) 두께 배분. 나머지는 아래층부터 한 칸씩 — 깊은 층이 조금 더 두꺼워진다
        int[] thickness = new int[valid.Count];
        int assigned = 0;
        for (int i = 0; i < valid.Count; i++)
        {
            thickness[i] = mapHeight * valid[i].thicknessWeight / totalWeight;
            assigned += thickness[i];
        }
        for (int i = valid.Count - 1; i >= 0 && assigned < mapHeight; i--)
        {
            thickness[i]++;
            assigned++;
        }

        // 3) 목록 첫 항목이 맨 위이므로 Y는 위에서부터 내려가며 잘라 낸다
        int cursorTop = mapHeight - 1;
        for (int i = 0; i < valid.Count; i++)
        {
            if (thickness[i] <= 0) continue;

            int bottom = Mathf.Max(0, cursorTop - thickness[i] + 1);
            bands.Add(new Band(valid[i], bottom, cursorTop));

            for (int y = bottom; y <= cursorTop; y++)
                byY[y] = valid[i];

            cursorTop = bottom - 1;
            if (cursorTop < 0) break;
        }
    }

    /// <summary>해당 높이의 지층. 범위 밖이거나 지층이 없으면 null.</summary>
    public StrataDefinition At(int y)
    {
        if (y < 0 || y >= byY.Length) return null;
        return byY[y];
    }

    /// <summary>
    /// 해당 높이 지층의 두께(칸). 지층이 없으면 0.
    ///
    /// 경계를 흔들 때 <b>얇은 층이 뚫리지 않도록</b> 진폭을 제한하는 데 씁니다 —
    /// 15칸짜리 경계층에 ±12칸을 흔들면 위아래 층이 그대로 배어들어 장벽이 사라집니다.
    /// </summary>
    public int ThicknessAt(int y)
    {
        StrataDefinition def = At(y);
        if (def == null) return 0;

        foreach (var b in bands)
            if (b.definition == def && y >= b.bottomY && y <= b.topY) return b.Thickness;

        return 0;
    }

    /// <summary>디버그용 한 줄 요약</summary>
    public string Describe()
    {
        if (IsEmpty) return "지층 없음";

        var sb = new StringBuilder();
        for (int i = 0; i < bands.Count; i++)
        {
            if (i > 0) sb.Append(" / ");
            Band b = bands[i];
            sb.Append($"{b.definition.Label} y{b.bottomY}~{b.topY}({b.Thickness})");
        }
        return sb.ToString();
    }
}
