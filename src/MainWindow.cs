using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;

namespace NafanyaGameBypass
{
    internal sealed class GameRow : INotifyPropertyChanged
    {
        private bool enabled;
        public GameChoice Choice { get; private set; }
        public string Name { get { return Choice.Name; } }
        public string Location { get { return Choice.Location; } }
        public string Initial { get { return Name.Length > 0 ? Name.Substring(0, 1).ToUpperInvariant() : "?"; } }
        public string ToggleLabel { get { return "Прямое подключение для " + Name; } }
        public ImageSource Icon { get; private set; }
        public Visibility InitialVisibility { get { return Icon == null ? Visibility.Visible : Visibility.Collapsed; } }
        public Brush TileBrush { get; private set; }
        public string ModeLabel { get { return Enabled ? "Напрямую" : "По правилам Nafanya"; } }
        public Brush ModeBrush { get { return GlassUi.Brush(Enabled ? "#B3C9EE" : "#7E8BA8"); } }
        public event PropertyChangedEventHandler PropertyChanged;
        public event EventHandler Changed;
        public bool Enabled
        {
            get { return enabled; }
            set
            {
                if (enabled == value) return;
                enabled = value;
                Choice.Enabled = value;
                Notify("Enabled"); Notify("ModeLabel"); Notify("ModeBrush");
                if (Changed != null) Changed(this, EventArgs.Empty);
            }
        }

        public GameRow(GameChoice game)
        {
            Choice = new GameChoice { Name = game.Name, Location = game.Location, Pattern = game.Pattern, Enabled = game.Enabled };
            enabled = game.Enabled;
            string color = game.Name == "Fortnite" ? "#39567F" : game.Name == "League of Legends" ? "#60503A" : "#514774";
            TileBrush = new LinearGradientBrush((Color)ColorConverter.ConvertFromString(color), Color.FromArgb(75, 40, 49, 77), 45);
            string file = game.Location;
            if (Directory.Exists(file)) file = Path.Combine(file, "FortniteClient-Win64-Shipping.exe");
            try
            {
                if (File.Exists(file))
                {
                    using (var icon = System.Drawing.Icon.ExtractAssociatedIcon(file))
                    {
                        if (icon != null)
                        {
                            var image = Imaging.CreateBitmapSourceFromHIcon(icon.Handle, Int32Rect.Empty, BitmapSizeOptions.FromWidthAndHeight(32, 32));
                            image.Freeze();
                            Icon = image;
                        }
                    }
                }
            }
            catch (ArgumentException) { }
            catch (System.ComponentModel.Win32Exception) { }
        }

        private void Notify(string property)
        {
            if (PropertyChanged != null) PropertyChanged(this, new PropertyChangedEventArgs(property));
        }
    }

    internal sealed class MainWindow
    {
        private readonly RuleManager manager;
        private readonly bool preview;
        private readonly ObservableCollection<GameRow> rows = new ObservableCollection<GameRow>();
        private readonly ListBox games;
        private readonly Button apply;
        private readonly Button undo;
        private readonly TextBlock statusTitle;
        private readonly TextBlock statusDetail;
        private bool loading;
        public Window Window { get; private set; }

        public MainWindow(RuleManager manager, bool preview, SavedState previewState)
        {
            this.manager = manager;
            this.preview = preview;
            Window = GlassUi.LoadWindow("MainWindow.xaml");
            games = (ListBox)Window.FindName("Games");
            games.ItemsSource = rows;
            apply = (Button)Window.FindName("Apply");
            undo = (Button)Window.FindName("Undo");
            statusTitle = (TextBlock)Window.FindName("StatusTitle");
            statusDetail = (TextBlock)Window.FindName("StatusDetail");
            games.AddHandler(Button.ClickEvent, new RoutedEventHandler(delegate(object sender, RoutedEventArgs e)
            {
                var button = e.OriginalSource as Button;
                var row = button == null ? null : button.Tag as GameRow;
                if (row == null) return;
                rows.Remove(row);
                DraftChanged();
                e.Handled = true;
            }));
            GlassUi.Connect(Window, "AddProcess", delegate
            {
                var picker = new ProcessPicker();
                if (picker.ShowDialog(Window) != true) return;
                foreach (string file in picker.SelectedExecutables) AddExecutable(file);
            });
            GlassUi.Connect(Window, "AddFile", AddFiles);
            GlassUi.Connect(Window, "RefreshGames", delegate { Execute(delegate { Load(preview ? previewState : manager.Load()); }); });
            GlassUi.Connect(Window, "Inspect", delegate
            {
                Execute(delegate
                {
                    GlassUi.ShowDetails(Window, "Соединения игр", preview ? "Проверка соединений доступна в основной версии менеджера." :
                        string.Join(Environment.NewLine + Environment.NewLine, manager.SummarizeGameFlows()));
                });
            });
            GlassUi.Connect(Window, "Apply", delegate
            {
                Execute(delegate
                {
                    manager.Apply(manager.Prepare(rows.Select(row => row.Choice).ToList()));
                    undo.IsEnabled = true;
                    SetStatus("Правила применены", "Для уже открытой игры начните новое подключение. Резервная копия сохранена.", false);
                });
            });
            GlassUi.Connect(Window, "Undo", delegate
            {
                Execute(delegate { manager.RestoreLast(); Load(manager.Load()); SetStatus("Изменение отменено", "Восстановлены предыдущие правила и список игр.", false); });
            });
            Window.ContentRendered += delegate { Execute(delegate { Load(preview ? previewState : manager.Load()); }); };
        }

        private void Execute(Action action)
        {
            try { Mouse.OverrideCursor = Cursors.Wait; action(); }
            catch (Exception error)
            {
                string message = error is UnauthorizedAccessException ? "Для записи правил нужны права администратора." : error.Message;
                SetStatus("Требуется внимание", message, true);
                GlassUi.ShowDetails(Window, "Не удалось завершить", message);
            }
            finally { Mouse.OverrideCursor = null; }
        }

        internal void Load(SavedState state)
        {
            loading = true;
            try { rows.Clear(); foreach (var game in state.Games) AddRow(game); }
            finally { loading = false; }
            UpdateCounts();
            apply.IsEnabled = !preview;
            undo.IsEnabled = !preview && !string.IsNullOrEmpty(state.LastBackup);
            if (preview) SetStatus("Предпросмотр интерфейса", "Переключатели в этом окне меняют только видимый выбор.", false);
            else if (state.NeedsReapply) SetStatus("Правила обновлены Nafanya", "Нажмите «Применить», чтобы вернуть выбранные исключения.", true);
            else SetStatus("Все изменения сохранены", "Выбор применяется к новым соединениям игры.", false);
        }

        private void AddRow(GameChoice game)
        {
            var row = new GameRow(game);
            row.Changed += delegate { DraftChanged(); };
            rows.Add(row);
        }

        private void DraftChanged()
        {
            if (loading) return;
            UpdateCounts();
            SetStatus(preview ? "Предпросмотр выбора" : "Есть несохранённые изменения",
                preview ? "Применение правил отключено в предпросмотре." : "Нажмите «Применить», чтобы обновить маршруты выбранных игр.", true);
        }

        private void UpdateCounts()
        {
            int selected = rows.Count(row => row.Enabled);
            ((TextBlock)Window.FindName("BypassCount")).Text = selected.ToString();
            ((TextBlock)Window.FindName("BypassSuffix")).Text = Plural(selected);
            ((TextBlock)Window.FindName("GameCount")).Text = rows.Count.ToString();
            ((TextBlock)Window.FindName("CountCaption")).Text = "Выбрано " + selected + " из " + rows.Count;
            ((FrameworkElement)Window.FindName("EmptyState")).Visibility = rows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        private static string Plural(int count)
        {
            if (count % 100 >= 11 && count % 100 <= 14) return "игр";
            if (count % 10 == 1) return "игра";
            if (count % 10 >= 2 && count % 10 <= 4) return "игры";
            return "игр";
        }

        private void SetStatus(string title, string detail, bool pending)
        {
            statusTitle.Text = title;
            statusDetail.Text = detail;
            statusDetail.ToolTip = detail;
            ((System.Windows.Shapes.Ellipse)Window.FindName("StatusDot")).Fill = GlassUi.Brush(pending ? "#E3C09C" : "#91D6BF");
        }

        private void AddFiles()
        {
            var picker = new OpenFileDialog { Title = "Выберите игровой исполняемый файл", Filter = "Приложения Windows (*.exe)|*.exe", CheckFileExists = true, Multiselect = true };
            if (picker.ShowDialog(Window) != true) return;
            foreach (string file in picker.FileNames) AddExecutable(file);
        }

        private void AddExecutable(string file)
        {
            if (rows.Any(row => Regex.IsMatch(file, row.Choice.Pattern))) return;
            AddRow(new GameChoice { Name = Path.GetFileNameWithoutExtension(file), Location = file, Pattern = RuleManager.ExactPattern(file), Enabled = true });
            DraftChanged();
        }
    }
}
