using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Microsoft.Win32;

namespace IpInfo
{
    public partial class MainWindow : Window
    {
        public string DataDir { get; private set; }
        public IpDb Db { get; private set; }
        public string LoadError { get; private set; }
        public bool Loading { get; private set; }

        private List<ResultRow> _rows = new List<ResultRow>();
        private string _filter;
        private bool _busy;

        public MainWindow()
        {
            InitializeComponent();
            DataDir = IpDb.FindDataDir();
            DataObject.AddPastingHandler(Query, OnPaste);
        }

        private async void Window_Loaded(object sender, RoutedEventArgs e)
        {
            Query.Focus();
            await ReloadAsync();
        }

        // ================================================================ 데이터

        public async Task ReloadAsync()
        {
            Loading = true;
            LoadError = null;
            SetStatus(Brushes.Gray, "데이터 불러오는 중");
            if (_rows.Count == 0) ShowStart();

            if (IpDb.SourceCount(DataDir) == 0)
            {
                Db = null;
            }
            else
            {
                try
                {
                    string dir = DataDir;
                    Db = await Task.Run(() => IpDb.Load(dir));
                }
                catch (Exception ex)
                {
                    Db = null;
                    LoadError = ex.Message;
                }
            }
            Loading = false;
            UpdateStatus();
            if (_rows.Count == 0) ShowStart();
        }

        public (string date, int ageDays) DataAge()
        {
            var dates = IpDb.DescribeFiles(DataDir).Select(f => f.Date).Where(d => !string.IsNullOrEmpty(d)).ToList();
            if (dates.Count == 0) return ("", 0);
            string oldest = dates.Min();
            int age = DateTime.TryParse(oldest, out var d) ? (int)(DateTime.Today - d).TotalDays : 0;
            return (oldest, age);
        }

        private void UpdateStatus()
        {
            if (Db == null)
            {
                SetStatus((Brush)FindResource("Danger"), LoadError != null ? "데이터 오류" : "데이터 없음");
                return;
            }
            var (date, age) = DataAge();
            bool old = age > 90;
            string warn = !Db.HasGeo ? " (위치 DB 없음)" : old ? " (갱신 필요)" : "";
            SetStatus((Brush)FindResource(warn.Length > 0 ? "Amber" : "Teal"), $"데이터 {date}{warn}");
        }

        private void SetStatus(Brush dot, string text)
        {
            DataDot.Fill = dot;
            DataText.Text = text;
        }

        // ================================================================ 화면 전환

        private void ShowPanel(UIElement panel)
        {
            foreach (var p in new UIElement[] { IdlePanel, MessagePanel, SinglePanel, MultiPanel })
                p.Visibility = p == panel ? Visibility.Visible : Visibility.Collapsed;
        }

        private void ShowStart()
        {
            if (Loading)
            {
                IdleTitle.Text = "데이터를 불러오는 중입니다";
                IdleText.Text = "잠시만 기다려 주세요. 처음 한 번은 몇 초 걸립니다.";
                Examples.Visibility = Visibility.Collapsed;
                ShowPanel(IdlePanel);
                return;
            }
            if (Db == null)
            {
                if (LoadError != null)
                    ShowMessage("데이터를 읽지 못했습니다", LoadError, true, true);
                else
                    ShowMessage("조회할 데이터가 아직 없습니다", "IP 조회에는 공개 할당 데이터 6개 파일(약 40MB)이 필요합니다.", true, false);
                return;
            }
            IdleTitle.Text = "어느 나라 IP인지 바로 확인하세요";
            IdleText.Text = "IP를 넣으면 국가코드와 소속 기관을 보여 줍니다. 방화벽·서버 로그를 통째로 붙여넣거나 파일을 끌어다 놓아도 됩니다.";
            Examples.Visibility = Visibility.Visible;
            ShowPanel(IdlePanel);
        }

        private void ShowMessage(string title, string text, bool setup, bool error)
        {
            MsgTitle.Text = title;
            MsgTitle.Foreground = (Brush)FindResource(error ? "Danger" : "Ink");
            MsgText.Text = text ?? "";
            MsgText.Visibility = string.IsNullOrEmpty(text) ? Visibility.Collapsed : Visibility.Visible;
            SetupSteps.Visibility = setup ? Visibility.Visible : Visibility.Collapsed;
            SetupPath.Text = DataDir;
            ShowPanel(MessagePanel);
        }

        // ================================================================ 조회

        private async void Run(string text = null)
        {
            text = (text ?? Query.Text).Trim();
            if (text.Length == 0 || _busy) return;
            if (Loading) { ShowToast("데이터를 불러오는 중입니다. 잠시 후 다시 눌러 주세요"); return; }
            if (Db == null) { ShowStart(); return; }

            _busy = true;
            Mouse.OverrideCursor = Cursors.Wait;
            try
            {
                var db = Db;
                const int limit = 100000;
                var (results, skipped, truncated) = await Task.Run(() =>
                {
                    var ips = IpUtil.ExtractIps(text, out var sk, limit);
                    var res = ips.Select(ip => db.Lookup(ip)).Where(x => x != null).ToList();
                    return (res, sk, ips.Count >= limit);
                });

                _rows = results.Select(x => new ResultRow(x)).ToList();
                _filter = null;

                if (_rows.Count == 0)
                {
                    ShowMessage("IP 주소를 찾지 못했습니다",
                        skipped.Count > 0
                            ? "올바르지 않은 형식: " + string.Join(", ", skipped)
                            : "입력한 내용에 IPv4·IPv6 주소가 없습니다.",
                        false, true);
                    return;
                }

                string note = skipped.Count > 0
                    ? $"건너뜀 — IP 형식이 아닌 항목 {skipped.Count}개: {string.Join(", ", skipped)}"
                    : null;
                if (_rows.Count == 1) RenderSingle(_rows[0].R, note);
                else RenderMulti(note, truncated);
            }
            finally
            {
                Mouse.OverrideCursor = null;
                _busy = false;
            }
        }

        private void RenderSingle(LookupResult r, string note)
        {
            BigStampHost.Content = Stamps.Big(r.Country, r.CountryName, true);
            SingleIp.Text = r.Ip;
            SingleCountry.Text = r.CountryName;
            SingleSource.Text = "국가코드 출처: " + r.CountrySource;
            SingleFacts.ItemsSource = ResultRow.BuildFacts(r);
            SingleNote.Visibility = note == null ? Visibility.Collapsed : Visibility.Visible;
            SingleNoteText.Text = note ?? "";
            ShowPanel(SinglePanel);
        }

        private void RenderMulti(string note, bool truncated)
        {
            int nCountries = _rows.Select(x => x.Country).Distinct().Count();
            MultiTitle.Text = $"IP {_rows.Count:N0}개{(truncated ? " (최대치까지만)" : "")}";
            MultiSub.Text = $"{nCountries}개 국가";
            MultiNote.Visibility = note == null ? Visibility.Collapsed : Visibility.Visible;
            MultiNoteText.Text = note ?? "";
            BuildFilters();
            ApplyFilter();
            ShowPanel(MultiPanel);
        }

        private void BuildFilters()
        {
            Filters.Children.Clear();
            var all = new ToggleButton
            {
                Style = (Style)FindResource("Chip"), Tag = "", IsChecked = true, Padding = new Thickness(12, 5, 12, 5),
                Content = ChipContent(null, "전체", _rows.Count),
            };
            all.Click += Chip_Click;
            Filters.Children.Add(all);

            foreach (var g in _rows.GroupBy(x => x.Country).OrderByDescending(g => g.Count()).ThenBy(g => g.Key))
            {
                var chip = new ToggleButton
                {
                    Style = (Style)FindResource("Chip"), Tag = g.Key,
                    Content = ChipContent(g.Key, null, g.Count()),
                    ToolTip = g.First().CountryName,
                };
                chip.Click += Chip_Click;
                Filters.Children.Add(chip);
            }
        }

        private FrameworkElement ChipContent(string cc, string label, int count)
        {
            var sp = new StackPanel { Orientation = Orientation.Horizontal };
            if (cc != null) sp.Children.Add(Stamps.Mini(cc));
            else sp.Children.Add(new TextBlock { Text = label, FontSize = 14, VerticalAlignment = VerticalAlignment.Center });
            sp.Children.Add(new TextBlock
            {
                Text = count.ToString("N0"), FontSize = 14, Margin = new Thickness(8, 0, 0, 0),
                Foreground = (Brush)FindResource("Muted"), VerticalAlignment = VerticalAlignment.Center,
            });
            return sp;
        }

        private void Chip_Click(object sender, RoutedEventArgs e)
        {
            var tag = (string)((ToggleButton)sender).Tag;
            _filter = string.IsNullOrEmpty(tag) ? null : tag;
            foreach (ToggleButton t in Filters.Children)
                t.IsChecked = (string)t.Tag == (_filter ?? "");
            ApplyFilter();
        }

        private List<ResultRow> CurrentRows() =>
            _filter == null ? _rows : _rows.Where(x => x.Country == _filter).ToList();

        private void ApplyFilter()
        {
            ResultGrid.ItemsSource = CurrentRows();
            if (ResultGrid.Items.Count > 0) ResultGrid.ScrollIntoView(ResultGrid.Items[0]);
        }

        // ================================================================ 내보내기

        private static readonly string[] Cols =
            { "입력", "IP", "국가코드", "국가명", "국가코드출처", "위치국가", "등록국가", "구분", "소속", "ASN", "AS명", "등록기관", "할당대역" };

        private static string[] RowOf(LookupResult r) => new[]
        {
            r.Input, r.Ip, r.Country, r.CountryName, r.CountrySource, r.GeoCountry ?? "", r.RegCountry ?? "", r.Type, r.OrgText,
            r.Asn != null ? r.Asn.Asn.ToString() : "", r.Asn != null ? r.Asn.Name : "",
            r.Rir != null ? r.Rir.Registry : "", r.Rir != null ? r.Rir.Range : "",
        };

        private static string CsvField(string v)
        {
            v ??= "";
            return v.IndexOfAny(new[] { ',', '"', '\n', '\r' }) >= 0 ? "\"" + v.Replace("\"", "\"\"") + "\"" : v;
        }

        private void SaveCsv_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new SaveFileDialog
            {
                Filter = "CSV 파일 (*.csv)|*.csv",
                FileName = $"IP조회_{DateTime.Now:yyyyMMdd_HHmm}.csv",
            };
            if (dlg.ShowDialog(this) != true) return;
            var sb = new StringBuilder();
            sb.Append(string.Join(",", Cols)).Append("\r\n");
            foreach (var row in CurrentRows())
                sb.Append(string.Join(",", RowOf(row.R).Select(CsvField))).Append("\r\n");
            try
            {
                File.WriteAllText(dlg.FileName, sb.ToString(), new UTF8Encoding(true)); // 엑셀 한글용 BOM
                ShowToast("CSV로 저장했습니다");
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "저장하지 못했습니다.\n" + ex.Message, "IP 조회", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void CopyTable_Click(object sender, RoutedEventArgs e)
        {
            var sb = new StringBuilder();
            sb.Append(string.Join("\t", Cols)).Append("\r\n");
            foreach (var row in CurrentRows())
                sb.Append(string.Join("\t", RowOf(row.R))).Append("\r\n");
            CopyText(sb.ToString(), "표를 복사했습니다. 엑셀에 바로 붙여넣을 수 있습니다");
        }

        private void CopyIp_Click(object sender, RoutedEventArgs e) => CopyText(SingleIp.Text, "IP를 복사했습니다");

        private void CopyText(string text, string msg)
        {
            for (int i = 0; i < 5; i++)
            {
                try { Clipboard.SetText(text); ShowToast(msg); return; }
                catch (System.Runtime.InteropServices.COMException) { System.Threading.Thread.Sleep(50); }
            }
            ShowToast("클립보드를 쓸 수 없습니다. 다시 시도해 주세요");
        }

        // ================================================================ 알림

        public void ShowToast(string msg)
        {
            ToastText.Text = msg;
            var a = new DoubleAnimationUsingKeyFrames();
            a.KeyFrames.Add(new LinearDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(150))));
            a.KeyFrames.Add(new LinearDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(2000))));
            a.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(2250))));
            Toast.BeginAnimation(OpacityProperty, a);
        }

        // ================================================================ 입력 이벤트

        private void Run_Click(object sender, RoutedEventArgs e) => Run();

        private void Query_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter && (Keyboard.Modifiers & ModifierKeys.Shift) == 0)
            {
                e.Handled = true;
                Run();
            }
        }

        private void Query_TextChanged(object sender, TextChangedEventArgs e)
        {
            Placeholder.Visibility = Query.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        private void OnPaste(object sender, DataObjectPastingEventArgs e)
        {
            // 여러 줄을 붙여넣으면 바로 조회
            Dispatcher.BeginInvoke(new Action(() =>
            {
                if (Query.Text.Contains('\n')) Run();
            }), DispatcherPriority.Background);
        }

        private void Example_Click(object sender, RoutedEventArgs e)
        {
            Query.Text = (string)((Button)sender).Tag;
            Query.CaretIndex = Query.Text.Length;
            Run();
        }

        private void Clear_Click(object sender, RoutedEventArgs e)
        {
            Query.Clear();
            _rows = new List<ResultRow>();
            ResultGrid.ItemsSource = null;
            ShowStart();
            Query.Focus();
        }

        // ================================================================ 파일

        private void OpenFile_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFileDialog { Filter = "텍스트·로그·CSV (*.txt;*.log;*.csv;*.tsv)|*.txt;*.log;*.csv;*.tsv|모든 파일 (*.*)|*.*" };
            if (dlg.ShowDialog(this) == true) LoadFile(dlg.FileName);
        }

        private async void LoadFile(string path)
        {
            string text;
            try
            {
                text = await Task.Run(() => IpDb.ReadTextAuto(path));
            }
            catch (Exception ex)
            {
                ShowMessage("파일을 읽지 못했습니다", ex.Message, false, true);
                return;
            }
            Query.Text = text.Length > 4000 ? $"({Path.GetFileName(path)} 파일 내용)" : text;
            Run(text);
        }

        private static bool HasFile(DragEventArgs e) => e.Data.GetDataPresent(DataFormats.FileDrop);

        private void Window_DragEnter(object sender, DragEventArgs e)
        {
            if (!HasFile(e)) return;
            DropOverlay.Visibility = Visibility.Visible;
            e.Effects = DragDropEffects.Copy;
            e.Handled = true;
        }

        private void Window_DragOver(object sender, DragEventArgs e)
        {
            if (!HasFile(e)) return;
            e.Effects = DragDropEffects.Copy;
            e.Handled = true;
        }

        private void Window_DragLeave(object sender, DragEventArgs e)
        {
            // 창 안의 다른 요소로 옮겨갈 때도 불리므로, 실제로 창 밖으로 나갔을 때만 숨김
            var root = (FrameworkElement)Content;
            var p = e.GetPosition(root);
            if (p.X <= 0 || p.Y <= 0 || p.X >= root.ActualWidth || p.Y >= root.ActualHeight)
                DropOverlay.Visibility = Visibility.Collapsed;
        }

        private void Window_Drop(object sender, DragEventArgs e)
        {
            DropOverlay.Visibility = Visibility.Collapsed;
            if (!HasFile(e)) return;
            e.Handled = true;
            var files = (string[])e.Data.GetData(DataFormats.FileDrop);
            if (files != null && files.Length > 0 && File.Exists(files[0])) LoadFile(files[0]);
        }

        // ================================================================ 데이터 창

        private DataWindow OpenDataWindow()
        {
            var w = new DataWindow(this) { Owner = this };
            return w;
        }

        private void DataBtn_Click(object sender, RoutedEventArgs e) => OpenDataWindow().ShowDialog();

        private void Download_Click(object sender, RoutedEventArgs e)
        {
            var w = OpenDataWindow();
            w.Loaded += (s, _) => w.StartDownload();
            w.ShowDialog();
        }

        private async void Reload_Click(object sender, RoutedEventArgs e)
        {
            await ReloadAsync();
            ShowToast(Db != null ? "데이터를 다시 불러왔습니다" : "데이터가 없습니다");
        }
    }
}
