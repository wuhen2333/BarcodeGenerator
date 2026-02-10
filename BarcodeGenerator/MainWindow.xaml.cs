using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using ZXing;
using ZXing.Common;
using ZXing.Datamatrix;
using ZXing.Datamatrix.Encoder;
using ZXing.Windows.Compatibility;
using FormNotifyIcon = System.Windows.Forms.NotifyIcon;

namespace BarcodeGenerator
{
    public partial class MainWindow : Window
    {
        private FormNotifyIcon _notifyIcon;
        private readonly string _configPath = Path.Combine(
    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
    "BarcodeGenerator",
    "config.json");

        // 内存中维护的规则列表
        private List<string> _ruleList = new List<string>();

        public MainWindow()
        {
            InitializeComponent();
            LoadConfig();
            InitTrayIcon();
        }

        // ==================== 1. 核心生成与递增逻辑 ====================

        private void BtnNext_Click(object sender, RoutedEventArgs e)
        {
            if (CmbRules == null) return;

            string currentText = CmbRules.Text; // 获取当前输入的内容
            if (string.IsNullOrEmpty(currentText)) return;

            // 正则逻辑：找到末尾的数字并 +1
            var match = Regex.Match(currentText, @"^(.*?)(\d+)$");

            if (match.Success)
            {
                string prefix = match.Groups[1].Value;
                string numberStr = match.Groups[2].Value;

                if (long.TryParse(numberStr, out long number))
                {
                    number++;
                    string newNumberStr = number.ToString().PadLeft(numberStr.Length, '0');

                    // 更新输入框文本
                    CmbRules.Text = prefix + newNumberStr;
                }
            }
            // 触发生成图片
            GenerateBarcode();
        }

        private void GenerateBarcode()
        {
            // 1. 安全检查
            if (CmbRules == null || ImgBarcode == null || ComboType == null) return;

            string content = CmbRules.Text;
            if (string.IsNullOrEmpty(content))
            {
                ImgBarcode.Source = null;
                return;
            }

            // 2. 确定码制
            var format = BarcodeFormat.CODE_128;
            if (ComboType.SelectedItem is ComboBoxItem item)
            {
                switch (item.Content.ToString())
                {
                    case "二维码 (QR Code)": format = BarcodeFormat.QR_CODE; break;
                    case "DM码 (Data Matrix)": format = BarcodeFormat.DATA_MATRIX; break;
                }
            }

            try
            {
                // 3. 配置参数 (核心修改)
                EncodingOptions options;

                if (format == BarcodeFormat.DATA_MATRIX)
                {
                    // 针对 DataMatrix 的特殊配置
                    options = new DatamatrixEncodingOptions
                    {
                        Height = 500,
                        Width = 500,
                        Margin = 0,
                        PureBarcode = true, // 隐藏文字 (虽然DM码本来就不带文字)
                                            // 【关键】强制正方形
                        SymbolShape = SymbolShapeHint.FORCE_SQUARE
                    };
                }
                else
                {
                    // 针对 Code 128 和 QR 的配置
                    options = new EncodingOptions
                    {
                        // Code 128 设宽一点 (1500像素)，保证长字符串生成的条纹足够清晰
                        Width = format == BarcodeFormat.CODE_128 ? 1500 : 500,
                        Height = format == BarcodeFormat.CODE_128 ? 400 : 500,
                        Margin = 0,
                        // 【关键】设为 true 即可隐藏下方的文字
                        PureBarcode = true
                    };
                }

                var writer = new BarcodeWriter
                {
                    Format = format,
                    Options = options
                };

                // 4. 生成图片
                using (var bitmap = writer.Write(content))
                {
                    ImgBarcode.Source = System.Windows.Interop.Imaging.CreateBitmapSourceFromHBitmap(
                        bitmap.GetHbitmap(),
                        IntPtr.Zero,
                        Int32Rect.Empty,
                        BitmapSizeOptions.FromEmptyOptions());
                }
            }
            catch
            {
                // 忽略生成错误
            }
        }

        // ==================== 2. 规则管理 (增删改) ====================

        // 当用户手动打字时触发
        private void CmbRules_TextChanged(object sender, TextChangedEventArgs e)
        {
            GenerateBarcode();
        }

        // 当用户从下拉列表选择时触发
        private void CmbRules_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            // 稍作延迟以确保 Text 属性已更新，或者直接生成
            GenerateBarcode();
        }

        // 保存当前规则到列表
        private void BtnSaveRule_Click(object sender, RoutedEventArgs e)
        {
            string current = CmbRules.Text.Trim();
            if (string.IsNullOrEmpty(current)) return;

            if (!_ruleList.Contains(current))
            {
                _ruleList.Add(current);
                RefreshRuleList(); // 刷新下拉框数据源
                CmbRules.Text = current; // 保持当前选中
                SaveConfig(); // 持久化
                System.Windows.MessageBox.Show("规则已保存！", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        // 删除当前选中的规则
        private void BtnDeleteRule_Click(object sender, RoutedEventArgs e)
        {
            string current = CmbRules.Text;
            if (_ruleList.Contains(current))
            {
                _ruleList.Remove(current);
                RefreshRuleList();

                // 删完后，默认显示第一条或清空
                if (_ruleList.Count > 0)
                    CmbRules.Text = _ruleList[0];
                else
                    CmbRules.Text = "";

                SaveConfig();
            }
        }

        private void RefreshRuleList()
        {
            // 重新绑定数据源
            CmbRules.ItemsSource = null;
            CmbRules.ItemsSource = _ruleList;
        }

        // ==================== 3. 配置持久化 ====================

        private void SaveConfig()
        {
            var config = new AppConfig
            {
                SavedRules = _ruleList,
                LastUsedRule = CmbRules.Text,
                LastTypeIndex = ComboType.SelectedIndex,
                IsTopmost = this.Topmost,
                WindowWidth = this.WindowState == WindowState.Normal ? this.Width : this.RestoreBounds.Width,
                WindowHeight = this.WindowState == WindowState.Normal ? this.Height : this.RestoreBounds.Height
            };
            try
            {
                // === 新增这一段：确保文件夹存在 ===
                string dir = Path.GetDirectoryName(_configPath);
                if (!Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }
                // ==============================
                File.WriteAllText(_configPath, JsonSerializer.Serialize(config));
            }
            catch { }
        }

        private void LoadConfig()
        {
            if (File.Exists(_configPath))
            {
                try
                {
                    var json = File.ReadAllText(_configPath);
                    var config = JsonSerializer.Deserialize<AppConfig>(json);
                    if (config != null)
                    {
                        // 1. 恢复列表
                        _ruleList = config.SavedRules ?? new List<string>();
                        RefreshRuleList();

                        // 2. 恢复上次输入的内容
                        CmbRules.Text = config.LastUsedRule;

                        // 3. 恢复设置
                        ComboType.SelectedIndex = config.LastTypeIndex;
                        this.Topmost = config.IsTopmost;

                        // === 新增恢复逻辑 ===
                        // 简单的校验，防止保存了0导致窗口消失
                        if (config.WindowWidth > 100) this.Width = config.WindowWidth;
                        if (config.WindowHeight > 100) this.Height = config.WindowHeight;
                    }
                }
                catch { }
            }

            // 初始默认值
            if (string.IsNullOrEmpty(CmbRules.Text))
            {
                CmbRules.Text = "EXEM-5601350S000000000010";
            }
        }

        public class AppConfig
        {
            public List<string> SavedRules { get; set; } = new();
            public string LastUsedRule { get; set; } = "";
            public int LastTypeIndex { get; set; } = 0;
            public bool IsTopmost { get; set; } = true;

            public double WindowWidth { get; set; } = 500;
            public double WindowHeight { get; set; } = 400;
        }

        // ==================== 4. 窗口置顶与托盘 ====================

        private void Topmost_Checked(object sender, RoutedEventArgs e) => this.Topmost = true;
        private void Topmost_Unchecked(object sender, RoutedEventArgs e) => this.Topmost = false;

        private void InitTrayIcon()
        {
            _notifyIcon = new FormNotifyIcon();
            _notifyIcon.Text = "条码测试助手";
            _notifyIcon.Visible = true;

            try
            {
                // === 核心修复：手动加载资源 ===
                // 确保 logo.ico 的属性是 "Resource" (资源)
                Uri iconUri = new Uri("pack://application:,,,/logo.ico", UriKind.Absolute);

                // 尝试获取资源流
                var streamInfo = System.Windows.Application.GetResourceStream(iconUri);

                if (streamInfo != null)
                {
                    using (var stream = streamInfo.Stream)
                    {
                        // 设置托盘图标
                        _notifyIcon.Icon = new System.Drawing.Icon(stream);

                        // 注意：流被读取后指针会变，如果还需要给窗口用，需要重置位置或重新获取
                        // 这里我们简化处理，窗口图标也用这个流（BitmapFrame支持流）
                        stream.Seek(0, SeekOrigin.Begin);
                        this.Icon = BitmapFrame.Create(stream);
                    }
                }
                else
                {
                    throw new Exception("资源流为空");
                }
            }
            catch (Exception)
            {
                // === 兜底方案 ===
                // 如果上面任何一步失败（比如文件没把属性改成Resource），使用系统默认图标
                // 绝对不会崩溃
                _notifyIcon.Icon = System.Drawing.SystemIcons.Application;
            }

            // 双击恢复窗口
            _notifyIcon.DoubleClick += (s, e) =>
            {
                this.Show();
                this.WindowState = WindowState.Normal;
                this.Activate();
            };

            // 右键退出
            var contextMenu = new System.Windows.Forms.ContextMenuStrip();
            contextMenu.Items.Add("退出", null, (s, e) =>
            {
                _notifyIcon.Visible = false;
                _notifyIcon.Dispose();
                System.Windows.Application.Current.Shutdown();
            });
            _notifyIcon.ContextMenuStrip = contextMenu;
        }
        private void ComboType_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            GenerateBarcode();
        }
        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            SaveConfig();
            e.Cancel = true;
            this.Hide();
        }
    }
}