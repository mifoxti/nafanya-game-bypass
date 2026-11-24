using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Web.Script.Serialization;
using System.Windows;

namespace NafanyaGameBypass
{
    internal static class Program
    {
        [STAThread]
        private static int Main(string[] args)
        {
            bool preview = args.Length > 0 && args[0] == "--preview-ui";
            bool fresh;
            using (var mutex = new Mutex(true, @"Local\NafanyaGameBypassManager" + (preview ? ".Preview" : ""), out fresh))
            {
                if (!fresh)
                {
                    MessageBox.Show("Менеджер уже открыт.", "Игры и Nafanya");
                    return 1;
                }
                string resultFile = args.Length == 2 ? args[1] : null;
                try
                {
                    var manager = new RuleManager();
                    if (args.Length == 2 && (args[0] == "--apply-games" || args[0] == "--apply-games-ui"))
                    {
                        var state = manager.Load();
                        foreach (var game in state.Games)
                            if (game.Name == "League of Legends" || game.Name == "Fortnite") game.Enabled = true;
                        var prepared = manager.Prepare(state.Games);
                        string backup = manager.Apply(prepared);
                        File.WriteAllLines(resultFile, new[]
                        {
                            "Applied. Enabled games: " + string.Join(", ", state.Games.Where(game => game.Enabled).Select(game => game.Name)),
                            "Preserved original rules: " + prepared.PreservedRules,
                            "Runtime PID: " + prepared.RuntimePid,
                            "Before SHA256: " + RuleManager.Hash(prepared.Before),
                            "After SHA256: " + RuleManager.Hash(prepared.After),
                            "Backup: " + backup
                        });
                        if (args[0] == "--apply-games") return 0;
                    }
                    if (args.Length == 2 && args[0] == "--check")
                    {
                        var prepared = manager.Prepare(manager.Load().Games);
                        File.WriteAllLines(resultFile, new[]
                        {
                            "Prepared and compiled successfully; active file was only read.",
                            "Preserved original rules: " + prepared.PreservedRules,
                            "Enabled games: " + prepared.Patterns.Count,
                            "Runtime PID: " + prepared.RuntimePid,
                            "Current SHA256: " + RuleManager.Hash(prepared.Before),
                            "Candidate SHA256: " + RuleManager.Hash(prepared.After)
                        }.Concat(manager.SummarizeGameFlows()));
                        return 0;
                    }
                    SavedState previewState = null;
                    if (preview)
                    {
                        string path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NafanyaGameBypass", "games.json");
                        previewState = new JavaScriptSerializer().Deserialize<SavedState>(File.ReadAllText(path));
                    }
                    var application = new Application { ShutdownMode = ShutdownMode.OnMainWindowClose };
                    var main = new MainWindow(manager, preview, previewState);
                    application.Run(main.Window);
                    return 0;
                }
                catch (Exception error)
                {
                    string message = error is System.ComponentModel.Win32Exception || error is UnauthorizedAccessException
                        ? "Не хватает прав на доступ к правилам. Запустите менеджер от имени администратора."
                        : error is IOException
                            ? "Не удалось прочитать или записать локальный файл. Проверьте доступ к папке менеджера и Nafanya."
                            : error.Message;
                    if (resultFile != null) File.WriteAllText(resultFile, "ERROR " + message);
                    else MessageBox.Show(message, "Игры и Nafanya", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return 1;
                }
            }
        }
    }
}
