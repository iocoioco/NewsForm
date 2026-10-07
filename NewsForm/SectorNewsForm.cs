using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows.Forms;

namespace NewsForm
{
    public sealed class SectorNewsRequest
    {
        public int Version { get; set; }
        public string Sector { get; set; }
        public int Date { get; set; }
        public int PreviousDate { get; set; }
        public string[] Stocks { get; set; }
        public int MaxItems { get; set; }
        public DateTime CurrentDay => ParseDay(Date);
        public DateTime PreviousDay => ParseDay(PreviousDate);
        private static DateTime ParseDay(int value) => DateTime.ParseExact(value.ToString(CultureInfo.InvariantCulture), "yyyyMMdd", CultureInfo.InvariantCulture);
        public void Validate()
        {
            if (Version != 1 || string.IsNullOrWhiteSpace(Sector) || Stocks == null || Stocks.Length == 0)
                throw new InvalidDataException("섹터 뉴스 요청 정보가 없습니다.");
            if (PreviousDay >= CurrentDay) throw new InvalidDataException("직전 거래일이 올바르지 않습니다.");
            if (MaxItems < 1 || MaxItems > 20) throw new InvalidDataException("뉴스 표시 한도는 1~20개입니다.");
            Stocks = Stocks.Where(s => !string.IsNullOrWhiteSpace(s)).Select(s => s.Trim()).Distinct().ToArray();
            if (Stocks.Length == 0) throw new InvalidDataException("섹터 구성 종목이 없습니다.");
        }
        public static SectorNewsRequest Read(string path)
        {
            var request = new JavaScriptSerializer().Deserialize<SectorNewsRequest>(File.ReadAllText(path, Encoding.UTF8));
            if (request == null) throw new InvalidDataException("뉴스 요청을 읽을 수 없습니다.");
            request.Validate();
            // Only consume temporary handoff files; arbitrary standalone request files are preserved.
            string root = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "IK_BJSD", "NewsRequests")) + Path.DirectorySeparatorChar;
            if (Path.GetFullPath(path).StartsWith(root, StringComparison.OrdinalIgnoreCase)) File.Delete(path);
            return request;
        }
        public string CacheFile()
        {
            string key = Sector + "|" + Date + "|" + PreviousDate + "|" + string.Join("|", Stocks.OrderBy(s => s, StringComparer.Ordinal));
            using (var hash = SHA256.Create())
            {
                string name = BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes(key))).Replace("-", "");
                return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "IK_BJSD", "SectorNews", name + ".txt");
            }
        }
        public List<StockNewsItem> Filter(IEnumerable<StockNewsItem> articles)
        {
            var members = new HashSet<string>(Stocks, StringComparer.OrdinalIgnoreCase);
            return articles.Where(a => members.Contains(a.Stock)
                && DateTime.TryParseExact(a.Time, new[] { "yyyy/MM/dd HH:mm", "yyyy-MM-dd HH:mm" }, CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime time)
                && (time.Date == CurrentDay || time.Date == PreviousDay)).ToList();
        }
    }

    public static class SectorNewsSelection
    {
        private static readonly string[] Risk = { "정정", "부인", "취소", "철회", "거래정지", "상장폐지", "횡령", "배임", "부도", "회생절차", "생산 중단", "가동 중단", "화재" };
        private static readonly string[] Material = { "실적", "전망", "영업이익", "적자", "흑자", "수주", "공급계약", "공급 계약", "관세", "수출규제", "수출 규제", "유상증자", "인수", "합병", "설비투자", "설비 투자" };
        private static readonly string[] Industry = { "가격", "수요", "공급", "HBM", "인증", "신제품", "기술", "금리", "환율" };
        private static bool Has(NewsIssue issue, string[] words) => issue.Articles.Any(a => words.Any(w => a.Title.IndexOf(w, StringComparison.OrdinalIgnoreCase) >= 0));
        public static int Score(NewsIssue issue)
        {
            if (Has(issue, Risk)) return 100;
            if (Has(issue, Material)) return 80;
            if (issue.Articles.All(a => NewsView.IsRoutine(a.Title))) return 0;
            if (Has(issue, Industry)) return 50;
            return 0; // Do not fill twenty slots with unclassified routine or promotional stories.
        }
        private static string Reason(int score) => score == 100 ? "위험·정정·사업 중단" : score == 80 ? "실적·전망·사업·정책" : "산업 가격·수요·기술";
        public static List<NewsIssue> Select(IEnumerable<StockNewsItem> articles, int limit)
        {
            var ranked = NewsView.Build(articles.ToList(), "", false, false, false, "")
                .Where(i => Score(i) > 0).OrderByDescending(Score)
                .ThenByDescending(i => i.Stock.Split(new[] { ", " }, StringSplitOptions.RemoveEmptyEntries).Length)
                .ThenByDescending(i => i.Time, StringComparer.Ordinal).ThenBy(i => i.Url, StringComparer.Ordinal).ToList();
            var selected = new List<NewsIssue>();
            var stockCounts = new Dictionary<string, int>();
            foreach (var issue in ranked)
            {
                var stocks = issue.Stock.Split(new[] { ", " }, StringSplitOptions.RemoveEmptyEntries);
                if (stocks.Length == 1 && stockCounts.TryGetValue(stocks[0], out int count) && count >= 4) continue;
                issue.Category = Reason(Score(issue)) + " (제목 기준)";
                selected.Add(issue);
                foreach (string stock in stocks) stockCounts[stock] = stockCounts.TryGetValue(stock, out int value) ? value + 1 : 1;
                if (selected.Count == Math.Min(20, Math.Max(1, limit))) break;
            }
            return selected;
        }
    }

    public sealed class SectorNewsForm : Form
    {
        private readonly SectorNewsRequest request;
        private readonly DataGridView grid = new DataGridView { Dock = DockStyle.Fill, ReadOnly = true, AutoGenerateColumns = false,
            AllowUserToAddRows = false, AllowUserToDeleteRows = false, RowHeadersVisible = false,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect, AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.AllCells };
        private readonly Label status = new Label { Dock = DockStyle.Bottom, Height = 78, Padding = new Padding(8) };
        private readonly Button collect = new Button { Text = "섹터 뉴스 수집", AutoSize = true };
        private readonly Button cancel = new Button { Text = "수집 취소", AutoSize = true, Enabled = false };
        private readonly System.Media.SoundPlayer completionSound = new System.Media.SoundPlayer(@"C:\BJS\data work\소\done.WAV");
        private CancellationTokenSource cancellation;
        private List<StockNewsItem> source = new List<StockNewsItem>();
        private string collectionStatus = "저장 뉴스 우선 표시 · 창이 열리면 자동 수집";
        public SectorNewsForm(SectorNewsRequest request)
        {
            request.Validate(); this.request = request;
            Text = request.Sector + " 뉴스 | " + request.PreviousDay.ToString("yyyy-MM-dd") + " · " + request.CurrentDay.ToString("yyyy-MM-dd");
            Size = new Size(1300, 800); StartPosition = FormStartPosition.CenterScreen; Font = new Font("맑은 고딕", 10F);
            grid.DefaultCellStyle.WrapMode = DataGridViewTriState.True;
            AddColumn("Time", "기사 시각(KST)", 160); AddColumn("Stock", "관련 종목", 170);
            AddColumn("Category", "선정 이유", 240); AddColumn("Count", "관련 기사 수", 95); AddColumn("Title", "제목 · 클릭하여 원문", 500, true);
            grid.CellClick += (s, e) => {
                if (e.RowIndex < 0 || e.ColumnIndex < 0) return;
                var issue = grid.Rows[e.RowIndex].DataBoundItem as NewsIssue;
                if (issue == null) return;
                if (grid.Columns[e.ColumnIndex].DataPropertyName == "Count") ShowArticles(issue);
                else if (grid.Columns[e.ColumnIndex].DataPropertyName == "Title") OpenUrl(issue.Url);
            };
            var bar = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(6) };
            bar.Controls.Add(new Label { AutoSize = true, Text = request.Sector + " · " + request.PreviousDay.ToString("MM/dd") + " 및 " + request.CurrentDay.ToString("MM/dd")
                + " · 구성 " + request.Stocks.Length + "종목 · 최대 " + request.MaxItems + "개", Padding = new Padding(0, 6, 15, 0) });
            bar.Controls.Add(collect); bar.Controls.Add(cancel);
            var reload = new Button { Text = "저장 뉴스 새로고침", AutoSize = true };
            reload.Click += (s, e) => { if (cancellation == null) LoadSaved(); };
            bar.Controls.Add(reload);
            Controls.Add(grid); Controls.Add(status); Controls.Add(bar);
            collect.Click += async (s, e) => await CollectAsync();
            cancel.Click += (s, e) => cancellation?.Cancel();
            FormClosing += (s, e) => cancellation?.Cancel();
            Disposed += (s, e) => completionSound.Dispose();
            Shown += async (s, e) => { LoadSaved(); await CollectAsync(); };
        }
        private void AddColumn(string property, string heading, int width, bool fill = false) => grid.Columns.Add(new DataGridViewTextBoxColumn {
            DataPropertyName = property, HeaderText = heading, Width = width, AutoSizeMode = fill ? DataGridViewAutoSizeColumnMode.Fill : DataGridViewAutoSizeColumnMode.None });
        private void LoadSaved()
        {
            try
            {
                var articles = new List<StockNewsItem>();
                foreach (string path in new[] { @"C:\BJS\News\News.txt", request.CacheFile() })
                    if (File.Exists(path)) articles.AddRange(NewsView.Read(path));
                source = request.Filter(articles).GroupBy(a => a.Stock + "\t" + a.Url + "\t" + a.ClusterId).Select(g => g.First()).ToList();
                RefreshNews();
            }
            catch (Exception ex) { status.Text = "저장 뉴스 읽기 실패: " + ex.Message; }
        }
        private void RefreshNews()
        {
            var selected = SectorNewsSelection.Select(source, request.MaxItems);
            grid.DataSource = selected;
            status.Text = "기간 내 저장 원문 " + source.Select(a => a.Url).Distinct().Count() + "건 → 주요 사건 " + selected.Count + "개\n"
                + collectionStatus + "\n제목 키워드 기반 초기 선정 · 단일 종목 최대 4개 · 섹터 거시뉴스 검색은 후속 개선"
                + (source.Count == 0 ? " · 해당 기간 저장 뉴스 없음" : "");
        }
        private async Task CollectAsync()
        {
            if (cancellation != null) return;
            var cts = new CancellationTokenSource(); cancellation = cts; collect.Enabled = false; cancel.Enabled = true;
            var results = new List<StockNewsItem>(); var failures = new List<string>();
            var diagnostics = new List<string>();
            var savedSource = source.ToList(); var elapsed = Stopwatch.StartNew();
            try
            {
                DateTime now = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, TimeZoneInfo.FindSystemTimeZoneById("Korea Standard Time"));
                DateTime end = request.CurrentDay.AddDays(1).AddTicks(-1);
                if (end > now) end = now;
                if (request.PreviousDay > end) throw new InvalidOperationException("미래 날짜의 뉴스는 수집할 수 없습니다.");
                int completed = 0;
                var active = new Dictionary<string, int>();
                using (var slots = new SemaphoreSlim(3))
                {
                    // COM lookup and UI updates stay on the UI context; only network waits overlap.
                    var tasks = request.Stocks.Select(async stock => {
                        await slots.WaitAsync(cts.Token);
                        try
                        {
                            cts.Token.ThrowIfCancellationRequested(); active[stock] = 0;
                            Action<int> progress = page => {
                                active[stock] = page;
                                collectionStatus = "완료 " + completed + "/" + request.Stocks.Length + " · " + elapsed.Elapsed.ToString(@"mm\:ss")
                                    + " 경과 · " + string.Join(" / ", active.Select(item => item.Key + " " + item.Value + "페이지"));
                                if (!IsDisposed) RefreshNews();
                            };
                            try
                            {
                                Action<List<StockNewsItem>> showPage = articles => {
                                    cts.Token.ThrowIfCancellationRequested();
                                    // Each callback contains this stock's cumulative results; replace rather than duplicate them.
                                    results.RemoveAll(a => a.Stock == stock);
                                    results.AddRange(articles);
                                    source = request.Filter(savedSource.Concat(results)).GroupBy(a => a.Stock + "\t" + a.Url + "\t" + a.ClusterId).Select(g => g.Last()).ToList();
                                    if (!IsDisposed) RefreshNews();
                                };
                                var stockResult = await NaverNewsCrawler.CrawlSectorStockAsync(stock, request.PreviousDay, end, cts.Token, progress, showPage);
                                results.RemoveAll(a => a.Stock == stock);
                                results.AddRange(stockResult);
                                diagnostics.Add(stock + ": 원문 " + stockResult.Count + " / 두 날짜 일치 " + request.Filter(stockResult).Count);
                                source = request.Filter(savedSource.Concat(results)).GroupBy(a => a.Stock + "\t" + a.Url + "\t" + a.ClusterId).Select(g => g.Last()).ToList();
                            }
                            catch (PartialNewsException partial)
                            {
                                results.RemoveAll(a => a.Stock == stock);
                                results.AddRange(partial.Articles);
                                failures.Add(stock + ": " + partial.Message);
                                diagnostics.Add(stock + ": 부분 원문 " + partial.Articles.Count + " / 두 날짜 일치 " + request.Filter(partial.Articles).Count);
                            }
                            catch (OperationCanceledException) { throw; }
                            catch (Exception ex) { failures.Add(stock + ": " + ex.Message); }
                            source = request.Filter(savedSource.Concat(results)).GroupBy(a => a.Stock + "\t" + a.Url + "\t" + a.ClusterId).Select(g => g.Last()).ToList();
                            completed++; active.Remove(stock);
                            collectionStatus = "완료 " + completed + "/" + request.Stocks.Length + " · " + elapsed.Elapsed.ToString(@"mm\:ss")
                                + " 경과 · 수집 원문 " + results.Select(a => a.Url).Distinct().Count() + "건 · 실패 " + failures.Count + "종목";
                            if (!IsDisposed) RefreshNews();
                        }
                        finally { slots.Release(); }
                    }).ToArray();
                    await Task.WhenAll(tasks);
                }
                cts.Token.ThrowIfCancellationRequested();
                if (failures.Count == request.Stocks.Length && results.Count == 0) throw new InvalidOperationException("전체 수집 실패: " + string.Join(" / ", failures));
                source = request.Filter(savedSource.Concat(results)).GroupBy(a => a.Stock + "\t" + a.Url + "\t" + a.ClusterId).Select(g => g.Last()).ToList();
                string cache = request.CacheFile(); Directory.CreateDirectory(Path.GetDirectoryName(cache));
                NaverNewsCrawler.SaveSectorNews(cache, source);
                File.WriteAllLines(cache + ".status.txt", diagnostics.Concat(failures), Encoding.UTF8);
                collectionStatus = "조회 종료 · 정상 " + (request.Stocks.Length - failures.Count) + "/" + request.Stocks.Length + "종목 · "
                    + (failures.Count == 0 ? "실패 없음" : string.Join(" / ", failures)) + " · 총 " + elapsed.Elapsed.ToString(@"mm\:ss");
                if (!IsDisposed)
                {
                    try { completionSound.Load(); completionSound.Play(); }
                    catch (Exception soundError) { collectionStatus += " · 완료음 재생 실패: " + soundError.Message; }
                }
            }
            catch (OperationCanceledException) { source = savedSource; collectionStatus = "수집 취소 · 기존 저장 결과 유지"; }
            catch (Exception ex) { source = savedSource; collectionStatus = "수집 오류 · 기존 저장 결과 유지: " + ex.Message; }
            finally
            {
                cancellation = null; cts.Dispose();
                if (!IsDisposed) { collect.Enabled = true; cancel.Enabled = false; RefreshNews(); }
            }
        }
        private void ShowArticles(NewsIssue issue)
        {
            using (var detail = new Form { Text = "관련 기사 · " + issue.Title, Size = new Size(1100, 600), StartPosition = FormStartPosition.CenterParent })
            {
                var list = new DataGridView { Dock = DockStyle.Fill, ReadOnly = true, AllowUserToAddRows = false, AutoGenerateColumns = false, DataSource = issue.Articles };
                foreach (string property in new[] { "Stock", "Time", "Title" }) list.Columns.Add(new DataGridViewTextBoxColumn {
                    DataPropertyName = property, HeaderText = property == "Stock" ? "종목" : property == "Time" ? "시각" : "제목 · 클릭하여 원문", Width = property == "Title" ? 650 : 170 });
                list.CellClick += (s, e) => { if (e.RowIndex >= 0 && e.ColumnIndex >= 0) OpenUrl(((StockNewsItem)list.Rows[e.RowIndex].DataBoundItem).Url); };
                detail.Controls.Add(list); detail.ShowDialog(this);
            }
        }
        private void OpenUrl(string url)
        {
            if (!Uri.TryCreate(url, UriKind.Absolute, out Uri uri) || (uri.Scheme != "http" && uri.Scheme != "https")) return;
            try { Process.Start(new ProcessStartInfo { FileName = uri.AbsoluteUri, UseShellExecute = true }); }
            catch (Exception ex) { MessageBox.Show(this, ex.Message, "기사 열기 오류"); }
        }
    }
}