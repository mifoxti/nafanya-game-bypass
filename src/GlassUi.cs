using System;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Shell;

namespace NafanyaGameBypass
{
    internal static class GlassUi
    {
        private struct Margins { public int Left, Right, Top, Bottom; }
        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr window, int attribute, ref int value, int size);
        [DllImport("dwmapi.dll")]
        private static extern int DwmExtendFrameIntoClientArea(IntPtr window, ref Margins margins);

        private static string ReadResource(string name)
        {
            using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("NafanyaGameBypass." + name))
            {
                if (stream == null) throw new InvalidOperationException("Не найден ресурс интерфейса " + name);
                using (var reader = new StreamReader(stream)) return reader.ReadToEnd();
            }
        }

        public static Window LoadWindow(string name)
        {
            var window = (Window)XamlReader.Parse(ReadResource(name).Replace("<!--ThemeResources-->", ReadResource("GlassTheme.xaml")));
            window.Style = (Style)window.FindResource(typeof(Window));
            WindowChrome.SetWindowChrome(window, new WindowChrome
            {
                CaptionHeight = 0, CornerRadius = new CornerRadius(24),
                ResizeBorderThickness = new Thickness(7), GlassFrameThickness = new Thickness(-1),
                UseAeroCaptionButtons = false
            });
            window.SourceInitialized += delegate
            {
                IntPtr handle = new WindowInteropHelper(window).Handle;
                int dark = 1, round = 2, acrylic = 3;
                DwmSetWindowAttribute(handle, 20, ref dark, sizeof(int));
                DwmSetWindowAttribute(handle, 33, ref round, sizeof(int));
                DwmSetWindowAttribute(handle, 38, ref acrylic, sizeof(int));
                var source = HwndSource.FromHwnd(handle);
                if (source != null && source.CompositionTarget != null) source.CompositionTarget.BackgroundColor = Colors.Transparent;
                var margins = new Margins { Left = -1, Right = -1, Top = -1, Bottom = -1 };
                DwmExtendFrameIntoClientArea(handle, ref margins);
            };
            var title = window.FindName("TitleBar") as FrameworkElement;
            if (title != null) title.MouseLeftButtonDown += delegate(object sender, MouseButtonEventArgs e)
            {
                if (e.ClickCount == 2 && window.ResizeMode == ResizeMode.CanResize)
                    window.WindowState = window.WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
                else if (e.ButtonState == MouseButtonState.Pressed) window.DragMove();
            };
            Connect(window, "CloseWindow", delegate { window.Close(); });
            Connect(window, "Minimize", delegate { window.WindowState = WindowState.Minimized; });
            Connect(window, "Maximize", delegate { window.WindowState = window.WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized; });
            return window;
        }

        public static void Connect(Window window, string name, Action action)
        {
            var button = window.FindName(name) as Button;
            if (button != null) button.Click += delegate { action(); };
        }

        public static void ShowDetails(Window owner, string title, string text)
        {
            var details = LoadWindow("DetailsWindow.xaml");
            details.Title = title;
            details.Owner = owner;
            ((TextBlock)details.FindName("Heading")).Text = title;
            ((TextBox)details.FindName("Details")).Text = text;
            Connect(details, "Done", delegate { details.Close(); });
            details.ShowDialog();
        }

        public static Brush Brush(string value) { return (Brush)new BrushConverter().ConvertFromString(value); }
    }
}
