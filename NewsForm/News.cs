using HtmlAgilityPack;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace NewsForm
{
    public class StockNewsItem
    {
        public string Stock { get; set; }
        public string Time { get; set; }
        public string Title { get; set; }
        public string Url { get; set; }
        public string ClusterId { get; set; }
    }
    public static class NaverNewsCrawler
    {
        private static CPUTILLib.CpStockCode _cpstockcode;
        public static event Action<string> Progress;
        private static void Report(string message) { Progress?.Invoke(message); File.AppendAllText(Path.Combine(NewsDirectory, "crawl.log"), DateTime.Now.ToString("s") + " " + message + Environment.NewLine, Encoding.UTF8); }

        private const string StockListFile =
            @"C:\BJS\data work\시총.txt";

        private const string NewsDirectory =
            @"C:\BJS\News";

        private static readonly HttpClient _client =
            new HttpClient { Timeout = TimeSpan.FromSeconds(30) };

        private static bool _isRunning = false;

        public static bool IsRunning => _isRunning;

        public static async Task CrawlAsync(
            DateTime startTime,
            DateTime? endTime = null)
        {
            DateTime end =
                endTime ?? DateTime.Now;

            if (startTime > end)
                throw new ArgumentException("시작 시간이 종료 시간보다 늦습니다.");

            if (_isRunning)
                return;

            _isRunning = true;

            try
            {
                if (!File.Exists(StockListFile))
                    throw new FileNotFoundException("종목 목록 파일이 없습니다.", StockListFile);

                Directory.CreateDirectory(NewsDirectory);

                string saveFile =
                    Path.Combine(NewsDirectory, "News.txt");

                using (var runLock = new FileStream(Path.Combine(NewsDirectory, "crawl.lock"), FileMode.OpenOrCreate, FileAccess.Write, FileShare.None))
                {

                var stocks = LoadStockNames().Distinct().ToList();
                if (stocks.Count == 0) throw new InvalidDataException("종목 목록이 비어 있습니다.");
                int failed = 0;
                var failures = new List<string>();
                Report($"수집 시작 {startTime:yyyy/MM/dd HH:mm} ~ {end:yyyy/MM/dd HH:mm}, {stocks.Count}종목");

                var allNews =
                    new List<StockNewsItem>();

                var urlSet =
                    new HashSet<string>(
                        StringComparer.OrdinalIgnoreCase);

                int index = 0;

                foreach (string stock in stocks)
                {
                    index++;
                    Report($"[{index}/{stocks.Count}] {stock} 수집 중");

                    try
                    {
                        string code =
                            GetStockCode(stock);

                        if (string.IsNullOrEmpty(code))
                        {
                            failed++;
                            failures.Add(stock + ": 종목코드 조회 실패");
                            Report(stock + " 종목코드 조회 실패");
                            await Task.Delay(1000);
                            continue;
                        }

                        var news =
                            await CrawlStockAsync(
                                stock,
                                code,
                                startTime,
                                end);

                        foreach (var item in news)
                        {
                            if (urlSet.Add(item.Stock + "\t" + item.Url + "\t" + item.ClusterId))
                                allNews.Add(item);
                        }

                        Debug.WriteLine(
                            $"NEWS [{index}/{stocks.Count}] " +
                            $"{stock} : {news.Count}건");

                        int articles = news.Select(n => n.Url).Distinct().Count();
                        int issues = NewsView.Build(news, stock, false, true, false, "").Count;
                        Report($"[{index}/{stocks.Count}] {stock}: 원문 {articles}건 → 기본 표시 {issues}개 이슈 / 누적 원문 {allNews.Select(n => n.Url).Distinct().Count()}건");
                    }
                    catch (Exception ex)
                    {
                        failed++;
                        failures.Add(stock + ": " + ex.Message);
                        Report($"{stock} 오류: {ex.Message}");
                    }

                    await Task.Delay(800);
                }

                if (failed > 0)
                {
                    if (allNews.Count > 0) Save(Path.Combine(NewsDirectory, "News.partial.txt"), allNews);
                    if (failed == stocks.Count)
                        throw new InvalidOperationException("전체 종목 수집 실패. 기존 News.txt를 유지합니다. crawl.log를 확인하세요.");
                }
                Save(saveFile, allNews);
                string summary = $"{DateTime.Now:yyyy/MM/dd HH:mm} · 조회 {startTime:MM/dd HH:mm}~{end:MM/dd HH:mm} · {stocks.Count - failed}/{stocks.Count}종목 정상 · 실패 {failed}종목";
                File.WriteAllLines(Path.Combine(NewsDirectory, "News.status.txt"), new[] { summary }.Concat(failures), Encoding.UTF8);
                Report($"완료: {allNews.Count}건");

                Debug.WriteLine(
                    $"NEWS 완료 : {allNews.Count}건");

                MessageBox.Show(
                    $"뉴스 크롤 완료\n\n원문 {allNews.Select(n => n.Url).Distinct().Count():N0}건\n{summary}"
                        + (failed > 0 ? "\n\n정상 결과를 표시합니다. 실패 목록은 '수집 상태'에서 확인하세요." : ""),
                    "News",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
                }
            }
            finally
            {
                _isRunning = false;
            }
        }

        // =========================================================
        // 전체 종목 뉴스 수집
        // =========================================================




        // =========================================================
        // 한 종목
        // =========================================================
        public sealed class NewsResponse { public List<NewsCluster> clusters { get; set; } }
        public sealed class NewsCluster { public List<NewsArticle> items { get; set; } }
        public sealed class NewsArticle
        {
            public string officeId { get; set; }
            public string articleId { get; set; }
            public string datetime { get; set; }
            public string title { get; set; }
            public string ClusterId { get; set; }
        }
        internal static List<NewsArticle> ParsePage(string json)
        {
            var data = new System.Web.Script.Serialization.JavaScriptSerializer().Deserialize<NewsResponse>(json);
            if (data?.clusters == null) throw new InvalidDataException("네이버 뉴스 응답 구조가 변경되었습니다.");
            var result = new List<NewsArticle>();
            foreach (var cluster in data.clusters)
            {
                if (cluster.items == null || cluster.items.Count == 0) continue;
                var head = cluster.items[0];
                string key = head.officeId + ":" + head.articleId;
                foreach (var article in cluster.items) { article.ClusterId = key; result.Add(article); }
            }
            return result;
        }
        private static async Task<List<StockNewsItem>> CrawlStockAsync(string stock, string code, DateTime startTime, DateTime endTime)
        {
            var result = new List<StockNewsItem>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            var pages = new HashSet<string>(StringComparer.Ordinal);
            for (int page = 1; page <= 100; page++)
            {
                string url = "https://stock.naver.com/api/domestic/detail/news?itemCode=" + Uri.EscapeDataString(code)
                    + "&page=" + page + "&pageSize=20";
                var articles = ParsePage(await _client.GetStringAsync(url));
                if (articles.Count == 0) return result;
                string signature = string.Join("|", articles.Select(a => a.officeId + ":" + a.articleId));
                if (!pages.Add(signature)) throw new InvalidDataException("뉴스 페이지가 반복됩니다.");
                bool allOlder = true;
                foreach (var article in articles)
                {
                    if (!DateTime.TryParseExact(article.datetime, "yyyyMMddHHmm", System.Globalization.CultureInfo.InvariantCulture,
                        System.Globalization.DateTimeStyles.None, out DateTime time))
                        throw new InvalidDataException("뉴스 날짜 형식 오류");
                    if (time >= startTime) allOlder = false;
                    if (string.IsNullOrEmpty(article.officeId) || string.IsNullOrEmpty(article.articleId))
                        throw new InvalidDataException("뉴스 기사 주소가 없습니다.");
                    string id = article.officeId + ":" + article.articleId;
                    if (!seen.Add(id + ":" + article.ClusterId)) continue;

                    if (time < startTime || time > endTime) continue;
                    result.Add(new StockNewsItem { Stock = stock, ClusterId = article.ClusterId, Time = time.ToString("yyyy/MM/dd HH:mm"),
                        Title = WebUtility.HtmlDecode(article.title ?? "").Replace("\t", " ").Replace("\r", " ").Replace("\n", " "),
                        Url = "https://n.news.naver.com/mnews/article/" + article.officeId + "/" + article.articleId });
                }
                if (allOlder) return result;

                await Task.Delay(500);
            }
            throw new InvalidDataException("100페이지 수집 한도입니다. 조회 기간을 줄이세요.");
        }

        private static List<string> LoadStockNames()
        {
            var result =
                new List<string>();

            foreach (string raw in
         File.ReadAllLines(StockListFile, Encoding.GetEncoding(949)))
            {
                string line =
                    raw.Trim();

                if (string.IsNullOrEmpty(line))
                    continue;

                int pos =
                    line.LastIndexOf(' ');

                if (pos <= 0)
                    continue;

                string stock =
                    line.Substring(0, pos)
                        .Trim();

                if (!string.IsNullOrEmpty(stock))
                    result.Add(stock);
            }

            return result;
        }


        // =========================================================
        // 종목명 → 네이버 6자리 코드
        // =========================================================
        private static string GetStockCode(
            string stock)
        {
            // 네 기존 대신증권 변환 메소드 사용
            if (_cpstockcode == null) _cpstockcode = new CPUTILLib.CpStockCode();
            string code = _cpstockcode.NameToCode(stock);
            if (string.IsNullOrWhiteSpace(code)) code = _cpstockcode.NameToCode(stock.Replace('_', ' '));

            if (string.IsNullOrWhiteSpace(code))
                return null;

            code = code.Trim();

            // 대신증권 코드 A005930 → 005930
            if (code.Length == 7 &&
                (code[0] == 'A' ||
                 code[0] == 'Q'))
            {
                code =
                    code.Substring(1);
            }

            return code;
        }


        // =========================================================
        // txt 저장
        // =========================================================
        private static void Save(
            string file,
            List<StockNewsItem> news)
        {
            var sb =
                new StringBuilder();

            sb.AppendLine(
                "종목\t시간\t제목\tURL\t이슈ID");

            foreach (var x in news)
            {
                sb.Append(x.Stock);
                sb.Append('\t');

                sb.Append(x.Time);
                sb.Append('\t');

                sb.Append(x.Title);
                sb.Append('\t');

                sb.Append(x.Url);
                sb.Append('\t');
                sb.Append(x.ClusterId ?? "");
                sb.AppendLine();
            }

            string temporary = file + ".tmp";
            File.WriteAllText(temporary, sb.ToString(), Encoding.UTF8);
            if (File.Exists(file)) File.Replace(temporary, file, file + ".bak");
            else File.Move(temporary, file);
        }
    }
}
