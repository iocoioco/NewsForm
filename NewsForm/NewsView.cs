using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace NewsForm
{
    public sealed class NewsIssue
    {
        public string Stock { get; set; }
        public string FirstTime { get; set; }
        public string Time { get; set; }
        public string Title { get; set; }
        public string Url { get; set; }
        public string Category { get; set; }
        public int Count { get; set; }
        public List<StockNewsItem> Articles { get; set; }
    }

    public static class NewsView
    {
        private static readonly string[] Urgent = { "정정", "부인", "취소", "철회", "거래정지", "상장폐지", "횡령", "배임", "부도", "회생절차" };
        private static readonly string[] Financial = { "실적", "영업이익", "순이익", "적자", "흑자", "매출", "수주", "공급계약", "공급 계약", "계약 체결", "유상증자", "전환사채", "자사주", "배당", "인수", "합병", "분할", "공개매수", "경영권", "설비 투자", "설비투자", "투자 유치", "투자유치", "임상", "허가", "승인", "소송", "제재", "리콜", "파업", "화재", "가동 중단", "생산 중단" };
        private static readonly string[] Macro = { "기준금리", "금리 인하", "금리 인상", "FOMC", "관세", "수출 규제", "수출규제", "환율", "전쟁" };
        private static readonly string[] Promotion = { "캠페인", "봉사", "기부", "후원", "이벤트", "프로모션", "할인", "박람회", "전시회", "공부방", "선점", "사회공헌", "체험", "경품" };
        private static bool Has(string title, string[] words) => words.Any(w => title.IndexOf(w, StringComparison.OrdinalIgnoreCase) >= 0);
        private static bool Direct(StockNewsItem article) => article.Stock.Split(new[] { ", " }, StringSplitOptions.RemoveEmptyEntries)
            .Any(s => article.Title.IndexOf(s.Replace('_', ' '), StringComparison.OrdinalIgnoreCase) >= 0);
        private static bool IsUrgent(NewsIssue issue) => issue.Articles.Any(a => Has(a.Title, Urgent));
        private static int Importance(NewsIssue issue) => issue.Articles.Max(a => {
            bool direct = Direct(a), financial = Has(a.Title, Financial);
            if (Has(a.Title, Urgent)) return 100;
            if (Has(a.Title, Promotion) && !financial) return 0;
            if (financial) return direct ? 80 : 50;
            if (Has(a.Title, Macro)) return 60;
            return direct && !IsRoutine(a.Title) ? 30 : 0;
        });

        // Budget is based on the previous default view, before optional title filters.
        // Urgent changes are retained even when they exceed the one-fifth target.
        public static List<NewsIssue> BuildImportant(List<StockNewsItem> source, string stock, bool hideRoutine, bool directOnly, string keyword)
        {
            int baseline = Build(source, stock, false, true, false, keyword).Count;
            int target = (int)Math.Ceiling(baseline / 5.0);
            var candidates = Build(source, stock, false, false, directOnly, keyword)
                .Where(i => !hideRoutine || i.Articles.Any(a => !IsRoutine(a.Title) || Has(a.Title, Urgent) || Has(a.Title, Financial))).ToList();
            var urgent = candidates.Where(IsUrgent).ToList();
            var selected = urgent.Concat(candidates.Where(i => !IsUrgent(i) && Importance(i) > 0)
                .OrderByDescending(Importance).ThenByDescending(i => i.Time, StringComparer.Ordinal)
                .ThenBy(i => i.Url, StringComparer.Ordinal).Take(Math.Max(0, target - urgent.Count))).ToList();
            foreach (var issue in selected)
                issue.Category = IsUrgent(issue) ? "중요 변경·위험" : Importance(issue) >= 80 ? "실적·사업·재무" : Importance(issue) >= 50 ? "시장·사업 영향" : "종목 직접 뉴스";
            return selected.OrderByDescending(i => i.Time, StringComparer.Ordinal).ToList();
        }

        private static readonly string[] Material = { "정정", "부인", "취소", "철회", "거래정지", "상장폐지", "유상증자", "전환사채", "소송", "횡령", "배임", "공급계약", "실적", "수주" };
        private static readonly string[] Routine = { "[특징주]", "[시황]", "[개장시황]", "[마감시황]", "[주식초고수는", "거래량 상위", "거래대금 상위", "상승률 상위", "하락률 상위", "인기 검색", "종목체크" };
        public static bool IsRoutine(string title) => !Material.Any(k => title.Contains(k)) && Routine.Any(k => title.Contains(k));
        private static bool SeparateUpdate(string title) => new[] { "정정", "부인", "취소", "철회", "거래정지", "상장폐지" }.Any(k => title.Contains(k));
        public static List<StockNewsItem> Read(string file)
        {
            return File.ReadLines(file, Encoding.UTF8).Where(l => !l.StartsWith("종목\t"))
                .Select(l => l.Split('\t')).Where(a => a.Length >= 4)
                .Select(a => new StockNewsItem { Stock = a[0], Time = a[1], Title = a[2], Url = a[3], ClusterId = a.Length > 4 ? a[4] : "" }).ToList();
        }
        public static List<NewsIssue> Build(List<StockNewsItem> source, string stock, bool all, bool hideRoutine, bool directOnly, string keyword)
        {
            var rows = source.Where(a => stock == "전체 종목" || string.IsNullOrEmpty(stock) || a.Stock == stock).ToList();
            if (hideRoutine) rows = rows.Where(a => !IsRoutine(a.Title)).ToList();
            if (directOnly) rows = rows.Where(a => a.Title.IndexOf(a.Stock, StringComparison.OrdinalIgnoreCase) >= 0).ToList();
            if (!string.IsNullOrWhiteSpace(keyword)) rows = rows.Where(a => a.Title.IndexOf(keyword.Trim(), StringComparison.OrdinalIgnoreCase) >= 0).ToList();
            var parent = new Dictionary<string, string>(StringComparer.Ordinal);
            Func<string, string> find = null;
            find = key => { if (!parent.ContainsKey(key)) parent[key] = key; if (parent[key] != key) parent[key] = find(parent[key]); return parent[key]; };
            foreach (var a in rows)
            {
                string url = "u:" + a.Url;
                find(url);
                if (all || SeparateUpdate(a.Title)) continue;
                string key = string.IsNullOrWhiteSpace(a.ClusterId)
                    ? "t:" + a.Time.Split(' ')[0] + ":" + Regex.Replace(a.Title, @"\s+", "").ToUpperInvariant()
                    : "c:" + a.ClusterId;
                parent[find(url)] = find(key);
            }
            return rows.GroupBy(a => SeparateUpdate(a.Title) ? "update:" + a.Url : find("u:" + a.Url))
                .Select(group => {
                    var articles = group.GroupBy(a => a.Url).Select(g => new StockNewsItem {
                        Stock = string.Join(", ", g.Select(a => a.Stock).Distinct()), Time = g.First().Time,
                        Title = g.First().Title, Url = g.Key, ClusterId = g.First().ClusterId
                    }).OrderByDescending(a => a.Time, StringComparer.Ordinal).ToList();
                    var representative = articles[0];
                    var stocks = group.Select(a => a.Stock).Distinct().OrderBy(s => s).ToList();
                    bool direct = group.Any(a => a.Title.IndexOf(a.Stock, StringComparison.OrdinalIgnoreCase) >= 0);
                    return new NewsIssue { Stock = string.Join(", ", stocks), FirstTime = articles.Min(a => a.Time), Time = representative.Time,
                        Title = representative.Title, Url = representative.Url, Count = articles.Count, Articles = articles,
                        Category = SeparateUpdate(representative.Title) ? "정정·변경 확인" : direct ? "제목 직접 언급" : "시장·섹터/간접" };
                }).OrderByDescending(a => a.Time, StringComparer.Ordinal).ToList();
        }
    }
}
