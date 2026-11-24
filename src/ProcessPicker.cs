using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace NafanyaGameBypass
{
    internal sealed class RunningExecutable
    {
        public string Name { get; set; }
        public string Path { get; set; }
        public List<int> Pids { get; set; }
        public string PidLabel { get { return "PID " + string.Join(", ", Pids); } }
    }

    internal sealed class ProcessPicker
    {
        private List<RunningExecutable> entries = new List<RunningExecutable>();
        private readonly Window window;
        private readonly TextBox search;
        private readonly ListBox processes;
        private readonly TextBlock count;
        private readonly Button choose;
        public List<string> SelectedExecutables { get; private set; }
        internal Window Window { get { return window; } }

        public ProcessPicker()
        {
            window = GlassUi.LoadWindow("ProcessPicker.xaml");
            search = (TextBox)window.FindName("Search");
            processes = (ListBox)window.FindName("Processes");
            count = (TextBlock)window.FindName("Count");
            choose = (Button)window.FindName("Choose");
            SelectedExecutables = new List<string>();
            search.TextChanged += delegate { Filter(); };
            processes.SelectionChanged += delegate { choose.IsEnabled = processes.SelectedItems.Count > 0; };
            processes.MouseDoubleClick += delegate { Choose(); };
            GlassUi.Connect(window, "RefreshProcesses", RefreshProcesses);
            GlassUi.Connect(window, "Choose", Choose);
            GlassUi.Connect(window, "Cancel", delegate { window.DialogResult = false; });
            window.ContentRendered += delegate { RefreshProcesses(); search.Focus(); };
        }

        public bool? ShowDialog(Window owner)
        {
            window.Owner = owner;
            return window.ShowDialog();
        }

        internal static List<RunningExecutable> EnumerateProcesses()
        {
            var found = new Dictionary<string, RunningExecutable>(StringComparer.OrdinalIgnoreCase);
            int ownPid;
            using (var own = Process.GetCurrentProcess()) ownPid = own.Id;
            foreach (var process in Process.GetProcesses())
            {
                using (process)
                {
                    try
                    {
                        if (process.Id == ownPid) continue;
                        var module = process.MainModule;
                        if (module == null) continue;
                        string file = module.FileName;
                        if (!string.Equals(System.IO.Path.GetExtension(file), ".exe", StringComparison.OrdinalIgnoreCase)) continue;
                        RunningExecutable entry;
                        if (!found.TryGetValue(file, out entry))
                        {
                            entry = new RunningExecutable { Name = System.IO.Path.GetFileName(file), Path = file, Pids = new List<int>() };
                            found.Add(file, entry);
                        }
                        entry.Pids.Add(process.Id);
                    }
                    catch (Win32Exception) { }
                    catch (InvalidOperationException) { }
                }
            }
            return found.Values.OrderBy(entry => entry.Name, StringComparer.OrdinalIgnoreCase).ToList();
        }

        internal void RefreshProcesses() { entries = EnumerateProcesses(); Filter(); }

        private void Filter()
        {
            string query = search.Text.Trim();
            var matching = entries.Where(entry => entry.Name.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0 ||
                entry.Path.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0).ToList();
            processes.ItemsSource = matching;
            count.Text = matching.Count + " из " + entries.Count + " · несколько с Ctrl";
            choose.IsEnabled = processes.SelectedItems.Count > 0;
            ((FrameworkElement)window.FindName("SearchHint")).Visibility = query.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        private void Choose()
        {
            if (processes.SelectedItems.Count == 0) return;
            SelectedExecutables = processes.SelectedItems.Cast<RunningExecutable>().Select(entry => entry.Path).ToList();
            window.DialogResult = true;
        }
    }
}
