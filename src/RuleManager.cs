using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Web.Script.Serialization;

namespace NafanyaGameBypass
{
    public sealed class RuleManager
    {
        private readonly JavaScriptSerializer json = new JavaScriptSerializer { MaxJsonLength = 16000000 };
        private readonly string install;
        private readonly string core;
        private readonly string runtimeConfig;
        private readonly string data;
        private readonly string stateFile;
        private readonly string legacyLeaguePattern = @"(?i)^D:\\Riot Games\\League of Legends\\Game\\League of Legends\.exe$";
        private string ruleFile;

        public RuleManager()
        {
            install = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Nafanya VPS");
            core = Path.Combine(install, "sing-box.exe");
            runtimeConfig = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Nafanya VPS", "tun.json");
            data = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NafanyaGameBypass");
            stateFile = Path.Combine(data, "games.json");
            if (!File.Exists(core) || !File.Exists(runtimeConfig))
                throw new InvalidOperationException("Не найдена конфигурация Nafanya. Запустите Nafanya и подключите VPN.");
        }

        private static Dictionary<string, object> Map(object value)
        {
            var result = value as Dictionary<string, object>;
            if (result == null) throw new InvalidOperationException("Неожиданный формат конфигурации Nafanya.");
            return result;
        }

        private static object Get(Dictionary<string, object> map, string key)
        {
            object value;
            return map.TryGetValue(key, out value) ? value : null;
        }

        private static List<object> Items(object value)
        {
            if (value == null) return new List<object>();
            if (value is string) return new List<object> { value };
            var sequence = value as IEnumerable;
            if (sequence == null) return new List<object> { value };
            return sequence.Cast<object>().ToList();
        }

        private Dictionary<string, object> ReadConfig()
        {
            return Map(json.DeserializeObject(File.ReadAllText(runtimeConfig)));
        }

        private Dictionary<string, object> ValidateConfig()
        {
            var config = ReadConfig();
            var route = Map(Get(config, "route"));
            var sets = Items(Get(route, "rule_set")).Select(Map);
            var set = sets.SingleOrDefault(item => Convert.ToString(Get(item, "tag")) == "geoip-ru");
            if (set == null || Convert.ToString(Get(set, "type")) != "local" || Convert.ToString(Get(set, "format")) != "binary")
                throw new InvalidOperationException("Эта конфигурация Nafanya не поддерживает выбранный способ обновления правил.");
            ruleFile = Path.GetFullPath(Path.Combine(install, Convert.ToString(Get(set, "path"))));
            if (!string.Equals(ruleFile, Path.Combine(install, "srss", "geoip-ru.srs"), StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Nafanya использует другой файл правил. Требуется проверка совместимости.");
            var direct = Items(Get(config, "outbounds")).Select(Map)
                .Where(item => Convert.ToString(Get(item, "type")) == "direct")
                .Select(item => Convert.ToString(Get(item, "tag"))).ToList();
            var rules = Items(Get(route, "rules")).Select(Map);
            if (!rules.Any(rule => direct.Contains(Convert.ToString(Get(rule, "outbound"))) &&
                Items(Get(rule, "rule_set")).Select(Convert.ToString).Contains("geoip-ru")))
                throw new InvalidOperationException("В Nafanya включите категорию «Российские сайты», затем повторите применение.");
            if (Regex.IsMatch(json.Serialize(Get(config, "dns")), @"(?i)""geoip-ru"""))
                throw new InvalidOperationException("Файл geoip-ru участвует в правилах DNS. Нужна проверка конфигурации.");
            if (Convert.ToBoolean(Get(route, "auto_detect_interface")) != true)
                throw new InvalidOperationException("В этой конфигурации отсутствует автоматический выбор физического интерфейса.");
            if (!string.Equals(Convert.ToString(Get(ReadApi(config, "/configs"), "mode")), "rule", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Переключите Nafanya в режим маршрутизации Rule.");
            return config;
        }

        private Dictionary<string, object> ReadApi(Dictionary<string, object> config, string path)
        {
            var api = Map(Get(Map(Get(config, "experimental")), "clash_api"));
            Uri endpoint;
            if (!Uri.TryCreate("http://" + Convert.ToString(Get(api, "external_controller")), UriKind.Absolute, out endpoint) ||
                endpoint.Host != "127.0.0.1" || !string.IsNullOrEmpty(endpoint.UserInfo))
                throw new InvalidOperationException("Не найден локальный API Nafanya на 127.0.0.1.");
            var request = (HttpWebRequest)WebRequest.Create(new Uri(endpoint, path));
            request.Proxy = null;
            request.Timeout = 3000;
            string secret = Convert.ToString(Get(api, "secret"));
            if (!string.IsNullOrEmpty(secret)) request.Headers["Authorization"] = "Bearer " + secret;
            try
            {
                using (var response = request.GetResponse())
                using (var reader = new StreamReader(response.GetResponseStream()))
                    return Map(json.DeserializeObject(reader.ReadToEnd()));
            }
            catch (WebException)
            {
                throw new InvalidOperationException("Локальный API Nafanya недоступен. Проверьте, что VPN подключён.");
            }
        }

        private int RuntimePid()
        {
            var processes = Process.GetProcessesByName("sing-box");
            if (processes.Length != 1)
                throw new InvalidOperationException("Ожидался один процесс sing-box. Закройте другие экземпляры или повторите после подключения Nafanya.");
            try
            {
                if (!string.Equals(processes[0].MainModule.FileName, core, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("Запущенный sing-box принадлежит другому приложению.");
                return processes[0].Id;
            }
            catch (System.ComponentModel.Win32Exception)
            {
                throw new InvalidOperationException("Для проверки процесса и записи правил нужны права администратора.");
            }
            finally { foreach (var process in processes) process.Dispose(); }
        }

        private void RunCore(params string[] arguments)
        {
            var start = new ProcessStartInfo(core, string.Join(" ", arguments.Select(Quote)));
            start.UseShellExecute = false;
            start.CreateNoWindow = true;
            start.WorkingDirectory = install;
            start.RedirectStandardOutput = true;
            start.RedirectStandardError = true;
            using (var process = Process.Start(start))
            {
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();
                if (!process.WaitForExit(15000))
                {
                    process.Kill();
                    throw new InvalidOperationException("Проверка правил sing-box превысила время ожидания.");
                }
                if (process.ExitCode != 0)
                    throw new InvalidOperationException("sing-box отклонил файл правил. Действующие правила не обновлены.");
            }
        }

        private static string Quote(string text)
        {
            if (text.Contains("\"") || text.EndsWith("\\"))
                throw new InvalidOperationException("Неожиданный путь при запуске проверки sing-box.");
            return "\"" + text + "\"";
        }

        public static string ExactPattern(string file)
        {
            return "(?i)^" + Regex.Escape(Path.GetFullPath(file)).Replace("\\ ", " ") + "$";
        }

        private static string FortnitePattern(string directory)
        {
            return "(?i)^" + Regex.Escape(Path.GetFullPath(directory)).Replace("\\ ", " ") +
                @"\\Fortnite(?:Client-Win64-Shipping(?:_[A-Za-z0-9]+)*|Launcher)\.exe$";
        }

        private List<GameChoice> Defaults()
        {
            var games = new List<GameChoice>();
            var league = new[] { @"D:\Riot Games\League of Legends\Game\League of Legends.exe", @"C:\Riot Games\League of Legends\Game\League of Legends.exe" }
                .FirstOrDefault(File.Exists);
            if (league != null) games.Add(new GameChoice { Name = "League of Legends", Location = league, Pattern = ExactPattern(league) });
            var fortnite = new[] { @"D:\Fortnite\Fortnite", @"D:\Fortnite", @"D:\Epic Games\Fortnite", @"C:\Program Files\Epic Games\Fortnite" }
                .FirstOrDefault(folder => Directory.Exists(Path.Combine(folder, ".egstore")) || Directory.Exists(Path.Combine(folder, "FortniteGame", "Binaries", "Win64")));
            if (fortnite != null)
            {
                string binaries = Path.Combine(fortnite, "FortniteGame", "Binaries", "Win64");
                games.Add(new GameChoice
                {
                    Name = "Fortnite",
                    Location = binaries,
                    Pattern = FortnitePattern(binaries)
                });
            }
            return games;
        }

        private Dictionary<string, object> Decode(byte[] bytes, string scratch)
        {
            string binary = Path.Combine(scratch, "rules.srs");
            string source = Path.Combine(scratch, "rules.json");
            File.WriteAllBytes(binary, bytes);
            RunCore("rule-set", "decompile", binary, "-o", source);
            return Map(json.DeserializeObject(File.ReadAllText(source)));
        }

        private List<string> ProcessPatterns(Dictionary<string, object> rule)
        {
            if (rule.Count != 1 || !rule.ContainsKey("process_path_regex")) return new List<string>();
            return Items(rule["process_path_regex"]).Select(Convert.ToString).ToList();
        }

        private string Canonical(object value)
        {
            var map = value as Dictionary<string, object>;
            if (map != null)
                return "{" + string.Join(",", map.OrderBy(pair => pair.Key, StringComparer.Ordinal).Select(pair => json.Serialize(pair.Key) + ":" + Canonical(pair.Value))) + "}";
            if (value != null && !(value is string) && value is IEnumerable)
                return "[" + string.Join(",", ((IEnumerable)value).Cast<object>().Select(Canonical)) + "]";
            return json.Serialize(value);
        }

        private SavedState ReadState()
        {
            if (File.Exists(stateFile))
            {
                var saved = json.Deserialize<SavedState>(File.ReadAllText(stateFile));
                if (saved == null || saved.Version != 1 || saved.Games == null || saved.ManagedPatterns == null)
                    throw new InvalidOperationException("Не удалось прочитать список игр менеджера.");
                return saved;
            }
            var state = new SavedState { Games = Defaults() };
            var activeBytes = File.ReadAllBytes(ruleFile);
            string scratch = NewScratch();
            try
            {
                var decoded = Decode(activeBytes, scratch);
                var existing = Items(Get(decoded, "rules")).Select(Map).SelectMany(ProcessPatterns).ToList();
                foreach (var game in state.Games) game.Enabled = existing.Contains(game.Pattern);
            }
            finally { Directory.Delete(scratch, true); }
            return state;
        }

        private string NewScratch()
        {
            string folder = Path.Combine(data, "scratch", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(folder);
            return folder;
        }

        public SavedState Load()
        {
            ValidateConfig();
            RuntimePid();
            var state = ReadState();
            state.NeedsReapply = !string.IsNullOrEmpty(state.LastAppliedHash) &&
                Hash(File.ReadAllBytes(ruleFile)) != state.LastAppliedHash;
            return state;
        }

        private static void ValidateChoice(GameChoice game)
        {
            if (string.IsNullOrWhiteSpace(game.Name) || string.IsNullOrWhiteSpace(game.Location) || string.IsNullOrWhiteSpace(game.Pattern) ||
                !Path.IsPathRooted(game.Location) || game.Pattern.Length > 1200 || !game.Pattern.StartsWith("(?i)^") || !game.Pattern.EndsWith("$"))
                throw new InvalidOperationException("Неверный путь к игре. Добавьте исполняемый файл ещё раз.");
            if (game.Pattern != ExactPattern(game.Location) && game.Pattern != FortnitePattern(game.Location))
                throw new InvalidOperationException("Правило игры не соответствует выбранному пути. Добавьте файл заново.");
        }

        public PreparedChange Prepare(List<GameChoice> choices)
        {
            ValidateConfig();
            int pid = RuntimePid();
            string configHash = Hash(File.ReadAllBytes(runtimeConfig));
            var previous = ReadState();
            var enabled = choices.Where(game => game.Enabled).ToList();
            foreach (var game in choices) ValidateChoice(game);
            var patterns = enabled.Select(game => game.Pattern).Distinct(StringComparer.Ordinal).OrderBy(item => item, StringComparer.Ordinal).ToList();
            byte[] before = File.ReadAllBytes(ruleFile);
            string scratch = NewScratch();
            try
            {
                var decoded = Decode(before, scratch);
                var kept = new List<object>();
                foreach (var entry in Items(Get(decoded, "rules")))
                {
                    var known = ProcessPatterns(Map(entry));
                    bool legacy = known.Count == 1 && known[0] == legacyLeaguePattern;
                    bool managed = known.Count > 0 && previous.ManagedPatterns.Count > 0 &&
                        known.OrderBy(item => item, StringComparer.Ordinal).SequenceEqual(previous.ManagedPatterns.OrderBy(item => item, StringComparer.Ordinal));
                    if (!legacy && !managed) kept.Add(entry);
                }
                var expected = kept.Select(Canonical).ToList();
                if (patterns.Count > 0)
                    kept.Add(new Dictionary<string, object> { { "process_path_regex", patterns.ToArray() } });
                decoded["rules"] = kept;
                string source = Path.Combine(scratch, "new.json");
                string binary = Path.Combine(scratch, "new.srs");
                File.WriteAllText(source, json.Serialize(decoded), new UTF8Encoding(false));
                RunCore("rule-set", "compile", source, "-o", binary);
                byte[] after = File.ReadAllBytes(binary);
                var roundTrip = Decode(after, scratch);
                var checkedRules = Items(Get(roundTrip, "rules"));
                if (patterns.Count > 0)
                {
                    var actualPatterns = ProcessPatterns(Map(checkedRules.Last())).OrderBy(item => item, StringComparer.Ordinal);
                    if (!actualPatterns.SequenceEqual(patterns)) throw new InvalidOperationException("Проверка нового правила не пройдена.");
                    checkedRules.RemoveAt(checkedRules.Count - 1);
                }
                if (!checkedRules.Select(Canonical).SequenceEqual(expected))
                    throw new InvalidOperationException("После компиляции изменились исходные правила. Применение остановлено.");
                return new PreparedChange
                {
                    Before = before, After = after, ConfigHash = configHash, RuntimePid = pid,
                    Patterns = patterns, PreviousState = previous, Choices = choices,
                    PreservedRules = expected.Count
                };
            }
            finally { Directory.Delete(scratch, true); }
        }

        public static string Hash(byte[] bytes)
        {
            using (var sha = SHA256.Create())
                return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", "");
        }

        private static void WriteAll(FileStream stream, byte[] bytes)
        {
            stream.Position = 0;
            stream.Write(bytes, 0, bytes.Length);
            stream.SetLength(bytes.Length);
            stream.Flush(true);
        }

        private void ReplaceRules(byte[] expected, byte[] replacement)
        {
            using (var stream = new FileStream(ruleFile, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite | FileShare.Delete))
            {
                if (stream.Length == 0 || stream.Length > 16000000)
                    throw new InvalidOperationException("Файл правил имеет неожиданный размер.");
                long length = Math.Max(stream.Length, replacement.Length);
                stream.Lock(0, length);
                try
                {
                    var current = new byte[stream.Length];
                    int offset = 0;
                    while (offset < current.Length)
                    {
                        int count = stream.Read(current, offset, current.Length - offset);
                        if (count == 0) throw new IOException("Не удалось прочитать файл правил целиком.");
                        offset += count;
                    }
                    if (!current.SequenceEqual(expected))
                        throw new InvalidOperationException("Nafanya обновила файл правил. Нажмите «Обновить список» и примените повторно.");
                    try { WriteAll(stream, replacement); }
                    catch { WriteAll(stream, expected); throw; }
                }
                finally { stream.Unlock(0, length); }
            }
            if (Hash(File.ReadAllBytes(ruleFile)) != Hash(replacement))
                throw new IOException("Контрольная сумма записанного файла не совпала.");
        }

        private void SaveState(SavedState state)
        {
            Directory.CreateDirectory(data);
            string temporary = Path.Combine(data, Guid.NewGuid().ToString("N") + ".json");
            File.WriteAllText(temporary, json.Serialize(state), new UTF8Encoding(false));
            if (File.Exists(stateFile)) File.Replace(temporary, stateFile, null);
            else File.Move(temporary, stateFile);
        }

        public string Apply(PreparedChange change)
        {
            ValidateConfig();
            if (RuntimePid() != change.RuntimePid || Hash(File.ReadAllBytes(runtimeConfig)) != change.ConfigHash)
                throw new InvalidOperationException("Nafanya переподключилась во время подготовки. Повторите применение.");
            string backups = Path.Combine(data, "backups");
            Directory.CreateDirectory(backups);
            string stem = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff") + "-" + Guid.NewGuid().ToString("N").Substring(0, 8);
            string backup = Path.Combine(backups, stem + ".srs");
            string stateBackup = Path.Combine(backups, stem + ".json");
            File.WriteAllBytes(backup, change.Before);
            File.WriteAllText(stateBackup, json.Serialize(change.PreviousState), new UTF8Encoding(false));
            ReplaceRules(change.Before, change.After);
            try
            {
                Thread.Sleep(500);
                ValidateConfig();
                if (RuntimePid() != change.RuntimePid)
                    throw new InvalidOperationException("Процесс Nafanya изменился во время проверки.");
                SaveState(new SavedState
                {
                    Games = change.Choices, ManagedPatterns = change.Patterns,
                    LastBackup = backup, LastStateBackup = stateBackup, LastAppliedHash = Hash(change.After),
                    LastBackupHash = Hash(change.Before)
                });
            }
            catch
            {
                ReplaceRules(change.After, change.Before);
                throw;
            }
            return backup;
        }

        public void RestoreLast()
        {
            ValidateConfig();
            var state = ReadState();
            string backupRoot = Path.GetFullPath(Path.Combine(data, "backups")) + Path.DirectorySeparatorChar;
            if (string.IsNullOrEmpty(state.LastBackup) || string.IsNullOrEmpty(state.LastStateBackup) ||
                !Path.GetFullPath(state.LastBackup).StartsWith(backupRoot, StringComparison.OrdinalIgnoreCase) ||
                !Path.GetFullPath(state.LastStateBackup).StartsWith(backupRoot, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Резервная копия предыдущего применения не найдена.");
            byte[] current = File.ReadAllBytes(ruleFile);
            if (Hash(current) != state.LastAppliedHash)
                throw new InvalidOperationException("Файл правил изменился после применения. Автоматический откат остановлен.");
            byte[] backup = File.ReadAllBytes(state.LastBackup);
            if (Hash(backup) != state.LastBackupHash)
                throw new InvalidOperationException("Контрольная сумма резервной копии не совпала.");
            var previous = json.Deserialize<SavedState>(File.ReadAllText(state.LastStateBackup));
            if (previous == null) throw new InvalidOperationException("Не удалось прочитать резервную копию списка игр.");
            ReplaceRules(current, backup);
            try { SaveState(previous); }
            catch { ReplaceRules(backup, current); throw; }
        }

        public string[] SummarizeGameFlows()
        {
            var config = ValidateConfig();
            var flows = Items(Get(ReadApi(config, "/connections"), "connections")).Select(Map);
            var state = ReadState();
            var direct = Items(Get(config, "outbounds")).Select(Map)
                .Where(item => Convert.ToString(Get(item, "type")) == "direct")
                .Select(item => Convert.ToString(Get(item, "tag"))).ToList();
            var lines = new List<string>();
            foreach (var game in state.Games)
            {
                var matching = flows.Where(flow =>
                {
                    string path = Convert.ToString(Get(Map(Get(flow, "metadata")), "processPath"));
                    return !string.IsNullOrEmpty(path) && Regex.IsMatch(path, game.Pattern);
                }).ToList();
                if (matching.Count == 0) lines.Add(game.Name + " — активных соединений пока нет");
                else
                {
                    int bypass = matching.Count(flow => Items(Get(flow, "chains")).Select(Convert.ToString).Any(direct.Contains));
                    lines.Add(game.Name + " — Direct " + bypass + " из " + matching.Count + " соединений");
                }
            }
            return lines.ToArray();
        }
    }
}
