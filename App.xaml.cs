using System.Windows;
using System.Windows.Threading;

namespace IpInfo
{
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            // 예상 못 한 오류로 창이 그냥 사라지지 않도록 내용을 보여 줌
            DispatcherUnhandledException += (s, ex) =>
            {
                MessageBox.Show("예상하지 못한 오류가 발생했습니다.\n\n" + ex.Exception.Message,
                    "IP 조회", MessageBoxButton.OK, MessageBoxImage.Error);
                ex.Handled = true;
            };
            base.OnStartup(e);
        }
    }
}
