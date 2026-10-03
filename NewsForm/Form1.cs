using New_Tradegy.Library.Utils;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
namespace NewsForm
{
    public partial class Form1 : Form
    {
        private const string NewsFile = @"C:\BJS\News\News.txt";
        private readonly DataGridView grid;
        private readonly ComboBox stock = new ComboBox { Width = 150, DropDownStyle = ComboBoxStyle.DropDownList };
        private readonly CheckBox all = new CheckBox { Text = "전체 기사", AutoSize = true };
        private readonly CheckBox important = new CheckBox { Text = "중요 뉴스 1/5", Checked = true, AutoSize = true };
        private readonly CheckBox routine = new CheckBox { Text = "반복 시황 숨김", Checked = true, AutoSize = true };
        private readonly CheckBox direct = new CheckBox { Text = "제목에 종목명 포함", AutoSize = true };
        private readonly TextBox keyword = new TextBox { Width = 140 };
        private readonly Label status = new Label { Dock = DockStyle.Bottom, Height = 90, Padding = new Padding(6) };
        private List<StockNewsItem> source = new List<StockNewsItem>();
        public Form1()
        {
            InitializeComponent(); Text = "News"; Width = 1450; Height = 1032; StartPosition = FormStartPosition.CenterScreen;
            Font = new Font("맑은 고딕", 10F); grid = CreateGrid();
            AddColumn(grid, "Stock", "관련 종목", 170);
            AddColumn(grid, "Category", "구분 (제목 기준)", 150);
            AddColumn(grid, "FirstTime", "최초", 145);
            AddColumn(grid, "Time", "최근", 145);
            AddColumn(grid, "Count", "기사 수 / 펼침", 105);
            AddColumn(grid, "Title", "대표 제목 · 클릭하여 원문", 300, true);
            grid.CellClick += (s, e) => {
                if (e.RowIndex < 0 || e.ColumnIndex < 0) return;
                var issue = grid.Rows[e.RowIndex].DataBoundItem as NewsIssue;
                if (issue == null) return;
                string column = grid.Columns[e.ColumnIndex].DataPropertyName;
                if (column == "Count" || column == "Stock") ShowArticles(issue);
                else if (column == "Title") OpenUrl(issue.Url);
            };
            var bar = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 40, WrapContents = false, AutoScroll = true, Padding = new Padding(4) };
            var crawl = new Button { Text = "새로 수집", AutoSize = true };
            crawl.Click += async (s, e) => await ChooseAndCrawl();
            var reload = new Button { Text = "파일 새로고침", AutoSize = true };
            reload.Click += (s, e) => LoadNews();
            var report = new Button { Text = "수집 상태", AutoSize = true };
            report.Click += (s, e) => MessageBox.Show(this,
                File.Exists(Path.ChangeExtension(NewsFile, ".status.txt")) ? File.ReadAllText(Path.ChangeExtension(NewsFile, ".status.txt")) : "수집 상태 기록이 없습니다.", "수집 상태");
            stock.Items.Add("전체 종목"); stock.SelectedIndex = 0;
            bar.Controls.AddRange(new Control[] { crawl, reload, report, stock, all, important, routine, direct,
                new Label { Text = "제목 검색", AutoSize = true, Padding = new Padding(0, 5, 0, 0) }, keyword });
            stock.SelectedIndexChanged += (s, e) => RefreshView();
            all.CheckedChanged += (s, e) => { important.Enabled = routine.Enabled = direct.Enabled = !all.Checked; RefreshView(); };
            important.CheckedChanged += (s, e) => RefreshView();
            routine.CheckedChanged += (s, e) => RefreshView(); direct.CheckedChanged += (s, e) => RefreshView();
            keyword.TextChanged += (s, e) => RefreshView();
            Controls.Add(grid); Controls.Add(status); Controls.Add(bar);
            Shown += async (s, e) => {
                var answer = MessageBox.Show(this, "새로 뉴스를 크롤링하시겠습니까?\n예: 수집 / 아니오: 기존 뉴스 보기", "News", MessageBoxButtons.YesNoCancel);
                if (answer == DialogResult.Yes) await ChooseAndCrawl(); else if (answer == DialogResult.No) LoadNews(); else Close();
            };
        }
        private static DataGridView CreateGrid() => new DataGridView {
            Dock = DockStyle.Fill, ReadOnly = true, AllowUserToAddRows = false, AllowUserToDeleteRows = false,
            RowHeadersVisible = false, AutoGenerateColumns = false, SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            MultiSelect = false, Font = new Font("맑은 고딕", 11F), AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.AllCells,
            DefaultCellStyle = new DataGridViewCellStyle { WrapMode = DataGridViewTriState.True }
        };
        private static void AddColumn(DataGridView g, string property, string title, int width, bool fill = false)
            => g.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = property, HeaderText = title, Width = width,
                AutoSizeMode = fill ? DataGridViewAutoSizeColumnMode.Fill : DataGridViewAutoSizeColumnMode.None });
        private async Task ChooseAndCrawl()
        {
            if (NaverNewsCrawler.IsRunning) { MessageBox.Show(this, "현재 수집 중입니다."); return; }
            using (var range = new NewsTimeRangeForm())
            {
                if (range.ShowDialog(this) != DialogResult.OK) return;
                Action<string> progress = message => { if (!IsDisposed) Text = "News - " + message; };
                NaverNewsCrawler.Progress += progress;
                try { await NaverNewsCrawler.CrawlAsync(range.StartTime, range.EndTime); if (!IsDisposed) LoadNews(); }
                catch (Exception ex) { if (!IsDisposed) MessageBox.Show(this, ex.Message, "뉴스 수집 오류", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
                finally { NaverNewsCrawler.Progress -= progress; if (!IsDisposed) Text = "News"; }
            }
        }
        private void LoadNews()
        {
            try {
                if (!File.Exists(NewsFile)) { MessageBox.Show(this, "News.txt 파일이 없습니다."); return; }
                var selection = stock.SelectedItem as string; source = NewsView.Read(NewsFile);
                stock.Items.Clear(); stock.Items.Add("전체 종목");
                stock.Items.AddRange(source.Select(a => a.Stock).Distinct().OrderBy(s => s).Cast<object>().ToArray());
                stock.SelectedItem = selection != null && stock.Items.Contains(selection) ? selection : "전체 종목";
                RefreshView();
            } catch (Exception ex) { MessageBox.Show(this, ex.Message, "뉴스 읽기 오류"); }
        }
        private void RefreshView()
        {
            string selected = stock.SelectedItem as string ?? "전체 종목";
            var original = source.Where(a => selected == "전체 종목" || a.Stock == selected).ToList();
            int count = original.Select(a => a.Url).Distinct().Count();
            var view = !all.Checked && important.Checked
                ? NewsView.BuildImportant(source, selected, routine.Checked, direct.Checked, keyword.Text)
                : NewsView.Build(source, selected, all.Checked, !all.Checked && routine.Checked, !all.Checked && direct.Checked, keyword.Text);
            grid.DataSource = view;
            double reduction = count == 0 ? 0 : (count - view.Count) * 100.0 / count;
            status.Text = $"원문 {count:N0}건 → 표시 {view.Count:N0}행 ({reduction:F1}% 감소) · 기사 수 클릭: 관련 기사 · 전체 기사: 숨김 해제"
                + (!all.Checked && important.Checked ? "\n제목 기준 중요도 선별 · 기존 기본 표시의 1/5 목표 · 중요 변경·위험은 한도 초과해도 표시" : "")
                + (original.Any(a => string.IsNullOrEmpty(a.ClusterId)) ? "\n기존 파일은 동일 제목만 묶습니다. 새로 수집하면 네이버 이슈 묶음과 관련 종목 연결이 적용됩니다." : "");
            string reportPath = Path.ChangeExtension(NewsFile, ".status.txt");
            if (File.Exists(reportPath)) status.Text += "\n" + File.ReadLines(reportPath).FirstOrDefault();
        }
        private void ShowArticles(NewsIssue issue)
        {
            using (var detail = new Form { Text = "관련 기사 - " + issue.Title, Width = 1250, Height = 650, StartPosition = FormStartPosition.CenterParent }) {
                var g = CreateGrid(); AddColumn(g, "Stock", "관련 종목", 220); AddColumn(g, "Time", "시간", 160);
                AddColumn(g, "Title", "제목 · 클릭하여 원문", 650, true); g.DataSource = issue.Articles;
                g.CellClick += (s, e) => {
                    if (e.RowIndex < 0 || e.ColumnIndex < 0) return;
                    var a = g.Rows[e.RowIndex].DataBoundItem as StockNewsItem; if (a == null) return;
                    if (e.ColumnIndex != 0) { OpenUrl(a.Url); return; }
                    var menu = new ContextMenuStrip();
                    foreach (string name in a.Stock.Split(new[] { ", " }, StringSplitOptions.RemoveEmptyEntries))
                        menu.Items.Add(name, null, (sender, args) => OpenStock(name));
                    menu.Closed += (sender, args) => menu.Dispose(); menu.Show(Cursor.Position);
                };
                detail.Controls.Add(g); detail.ShowDialog(this);
            }
        }
        private CPUTILLib.CpStockCode codes;
        private void OpenStock(string name)
        {
            try { if (codes == null) codes = new CPUTILLib.CpStockCode(); string code = codes.NameToCode(name);
                if (!string.IsNullOrWhiteSpace(code)) OpenUrl("https://stock.naver.com/domestic/stock/" + new string(code.Where(char.IsDigit).ToArray()) + "/news"); }
            catch (Exception ex) { MessageBox.Show(this, ex.Message, "종목 조회 오류"); }
        }
        private void OpenUrl(string url)
        {
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || (uri.Scheme != "https" && uri.Scheme != "http")) return;
            try { Process.Start(new ProcessStartInfo { FileName = uri.AbsoluteUri, UseShellExecute = true }); }
            catch (Exception ex) { MessageBox.Show(this, ex.Message, "기사 열기 오류"); }
        }
    }
}
