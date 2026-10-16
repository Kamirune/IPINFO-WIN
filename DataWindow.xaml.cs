using System;
using System.Diagnostics;
using System.IO;
using System.Windows;

namespace IpInfo
{
    public partial class DataWindow : Window
    {
        private readonly MainWindow _main;
        private bool _downloading;

        public DataWindow(MainWindow main)
        {
            InitializeComponent();
            _main = main;
            Refresh();
        }

        private void Refresh()
        {
            var db = _main.Db;
            bool ready = db != null;
            Counts.Visibility = ready ? Visibility.Visible : Visibility.Collapsed;
            NoData.Visibility = ready ? Visibility.Collapsed : Visibility.Visible;
            NoData.Text = _main.LoadError != null ? "데이터를 읽지 못했습니다: " + _main.LoadError : "불러온 데이터가 없습니다.";
            if (ready)
            {
                RirCount.Text = (db.RirCount[0] + db.RirCount[1]).ToString("N0");
                AsnCount.Text = (db.AsnCount[0] + db.AsnCount[1]).ToString("N0");
                CustomCount.Text = db.CustomCount.ToString("N0");
                GeoCount.Text = db.HasGeo ? (db.GeoCount[0] + db.GeoCount[1]).ToString("N0") : "없음";
            }
            NoGeo.Visibility = ready && !db.HasGeo ? Visibility.Visible : Visibility.Collapsed;
            Attribution.Text = "실제 위치 국가: " + IpDb.GeoAttribution + "   ·   등록 국가: APNIC·ARIN·RIPE NCC·LACNIC·AFRINIC   ·   소속: iptoasn.com";
            FileList.ItemsSource = IpDb.DescribeFiles(_main.DataDir);
            FolderPath.Text = _main.DataDir;
            FolderPath.ToolTip = _main.DataDir;
        }

        private async void Reload_Click(object sender, RoutedEventArgs e)
        {
            ReloadBtn.IsEnabled = false;
            await _main.ReloadAsync();
            ReloadBtn.IsEnabled = true;
            Refresh();
            _main.ShowToast(_main.Db != null ? "데이터를 다시 불러왔습니다" : "데이터가 없습니다");
        }

        private void Download_Click(object sender, RoutedEventArgs e) => StartDownload();

        public async void StartDownload()
        {
            if (_downloading) return;
            _downloading = true;
            DownloadBtn.IsEnabled = ReloadBtn.IsEnabled = false;
            Log.Visibility = Visibility.Visible;
            Log.Text = "받는 중입니다. 몇 분 걸릴 수 있습니다…\n";
            var progress = new Progress<string>(line =>
            {
                Log.AppendText(line + "\n");
                Log.ScrollToEnd();
            });
            try
            {
                int ok = await IpDb.DownloadAllAsync(_main.DataDir, progress);
                int total = IpDb.Downloads.Length;
                Log.AppendText($"\n{ok}/{total}개 파일을 받았습니다.");
                if (ok == 0)
                    Log.AppendText("\n인터넷에 연결되지 않은 것 같습니다. 인터넷이 되는 PC에서 받아 data 폴더를 옮겨 오세요.");
                if (ok > 0) await _main.ReloadAsync();
                Log.ScrollToEnd();
            }
            catch (Exception ex)
            {
                Log.AppendText("\n받지 못했습니다: " + ex.Message);
            }
            finally
            {
                _downloading = false;
                DownloadBtn.IsEnabled = ReloadBtn.IsEnabled = true;
                Refresh();
            }
        }

        private void OpenFolder_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Directory.CreateDirectory(_main.DataDir);
                Process.Start(new ProcessStartInfo("explorer.exe", "\"" + _main.DataDir + "\"") { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, "IP 조회", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void Close_Click(object sender, RoutedEventArgs e) => Close();

        protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
        {
            if (_downloading)
            {
                e.Cancel = true;
                _main.ShowToast("받는 중에는 닫을 수 없습니다");
            }
            base.OnClosing(e);
        }
    }
}
