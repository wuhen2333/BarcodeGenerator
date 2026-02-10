using System.Configuration;
using System.Data;
using System.Windows;
using System.Windows.Threading;

namespace BarcodeGenerator
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : System.Windows.Application
    {
        // 重写 OnStartup 方法
        protected override void OnStartup(StartupEventArgs e)
        {
            // 绑定全局异常捕获
            this.DispatcherUnhandledException += App_DispatcherUnhandledException;

            // 这一步也是为了单文件模式下的兼容性
            base.OnStartup(e);
        }

        private void App_DispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
        {
            // 一旦发生崩溃，弹出这个窗口显示错误信息
            System.Windows.MessageBox.Show("程序发生严重错误:\n" + e.Exception.Message + "\n\n" + e.Exception.StackTrace,
                            "崩溃提示", MessageBoxButton.OK, MessageBoxImage.Error);

            e.Handled = true; // 尝试阻止程序退出（可选）
        }
    }

}
