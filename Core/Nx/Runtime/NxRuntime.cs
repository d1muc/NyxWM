using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace nyxWM.Nx
{
    /// <summary>
    /// Owns the .nx documents, the surfaces they declare and the data providers
    /// they bind to.
    ///
    /// Frame model: the ticker thread advances the animation clock and samples the
    /// providers; the UI thread lays out and repaints. A file change sets a flag
    /// that the next frame notices on the UI thread, so a reload never races with
    /// a paint.
    /// </summary>
    internal sealed class NxRuntime : IDisposable
    {
        private readonly string[] _directories;
        private readonly NxDataRegistry _registry;
        private readonly string? _fallbackTheme;
        private readonly Color _autoAccent;
        private readonly List<NxSurfaceWindow> _windows = new();
        private readonly List<FileSystemWatcher> _watchers = new();
        private readonly List<string> _loadedFiles = new();

        private NxExpressionEvaluator _evaluator;

        private Action? _unsubscribe;
        private double _nowMs;
        private int _reloadPending;
        private int _started;
        private double _placementAccumulatorMs;
        private bool _disposed;

        /// <summary>The runtime the widgets talk back to.</summary>
        internal static NxRuntime? Current { get; private set; }

        internal NxRuntime(string[] directories, string? fallbackTheme = null, Color? autoAccent = null)
        {
            _directories = directories;
            _fallbackTheme = string.IsNullOrWhiteSpace(fallbackTheme) ? null : fallbackTheme;
            _autoAccent = autoAccent ?? Color.FromArgb(59, 130, 246);
            _registry = new NxDataRegistry();
            _evaluator = new NxExpressionEvaluator(_registry);
        }

        internal NxDataRegistry Registry => _registry;

        internal IReadOnlyList<string> LoadedFiles => _loadedFiles;

        internal int SurfaceCount => _windows.Count;

        /// <summary>Hides or shows every surface without destroying them.</summary>
        internal void SetGameMode(bool active)
        {
            foreach (NxSurfaceWindow window in _windows)
            {
                if (window.IsDisposed)
                    continue;

                try
                {
                    if (active)
                        window.Hide();
                    else
                        window.Show();
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"nx: game mode toggle failed: {ex.Message}");
                }
            }
        }

        /// <summary>Loads every document and starts the frame loop.</summary>
        internal void Start()
        {
            if (Interlocked.Exchange(ref _started, 1) == 1)
                return;

            Current = this;

            // Apply FPS from theme if set
            if (NxTheme.Current.Fps > 0)
                UiAnimationHost.RequestHz(NxTheme.Current.Fps);

            LoadDocuments(initial: true);
            WatchDirectories();

            // The ticker reports Active every frame: a bar that shows a clock and a
            // playing track changes continuously, so parking it would freeze time.
            _unsubscribe = UiAnimationHost.Subscribe(Advance, OnFrame);
        }

        internal void Stop()
        {
            Action? unsubscribe = Interlocked.Exchange(ref _unsubscribe, null);

            try
            {
                unsubscribe?.Invoke();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"NxRuntime: unsubscribe failed: {ex.Message}");
            }

            foreach (NxSurfaceWindow window in _windows)
            {
                try
                {
                    window.Hide();
                    window.Dispose();
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"NxRuntime: surface close failed: {ex.Message}");
                }
            }

            _windows.Clear();

            foreach (FileSystemWatcher watcher in _watchers)
            {
                try
                {
                    watcher.EnableRaisingEvents = false;
                    watcher.Dispose();
                }
                catch (Exception)
                {
                    // Watchers are best effort.
                }
            }

            _watchers.Clear();
        }

        // ------------------------------------------------------------- frame loop

        /// <summary>Runs on the ticker thread: sample providers and advance the clock.</summary>
        private void Advance(float deltaMs)
        {
            if (_disposed)
                return;

            _nowMs += deltaMs;
            _registry.Update(deltaMs);
            _placementAccumulatorMs += deltaMs;
        }

        /// <summary>Runs on the UI thread: lay out, repaint, and apply a pending reload.</summary>
        private void OnFrame()
        {
            if (_disposed)
                return;

            if (Interlocked.Exchange(ref _reloadPending, 0) == 1)
            {
                Reload();
                return;
            }

            // Placement is re-resolved a few times a second so a window that moved
            // monitors or a display that changed resolution is picked up.
            bool replace = _placementAccumulatorMs >= 1000.0;

            if (replace)
                _placementAccumulatorMs = 0.0;

            foreach (NxSurfaceWindow window in _windows)
            {
                if (window.IsDisposed)
                    continue;

                if (replace)
                    window.ApplyPlacement(MonitorFor(window));

                window.Frame();
            }
        }

        private static Rectangle MonitorFor(NxSurfaceWindow window)
        {
            try
            {
                return NxMonitor.ForIndex(window.Placement.Monitor, Cursor.Position);
            }
            catch (Exception)
            {
                return Screen.PrimaryScreen?.Bounds ?? new Rectangle(0, 0, 1920, 1080);
            }
        }

        // ----------------------------------------------------------------- loading

        private void LoadDocuments(bool initial)
        {
            var tokens = new Dictionary<string, NxValue>(StringComparer.OrdinalIgnoreCase);
            var animations = new Dictionary<string, NxAnimation>(StringComparer.OrdinalIgnoreCase);
            var surfaces = new List<(string Name, NxNode Node)>();
            var files = new List<string>();

            foreach (string directory in _directories)
            {
                if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
                    continue;

                string[] found;

                try
                {
                    found = Directory.GetFiles(directory, "*.nx", SearchOption.AllDirectories);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"NxRuntime: cannot list '{directory}': {ex.Message}");
                    continue;
                }

                Array.Sort(found, StringComparer.OrdinalIgnoreCase);

                foreach (string file in found)
                {
                    files.Add(file);

                    NxDocument? document = LoadDocument(file);

                    if (document == null)
                        continue;

                    // Tokens and animations are document-global: a theme file can
                    // define "$accent" once and every surface in every file uses it.
                    foreach (KeyValuePair<string, NxValue> token in document.TokenTable)
                        tokens[token.Key] = token.Value;

                    foreach (NxAnimation animation in document.Animations)
                        animations[animation.Name] = animation;

                    foreach (NxNode root in document.Roots)
                    {
                        if (!string.Equals(root.Type, "surface", StringComparison.OrdinalIgnoreCase))
                            continue;

                        surfaces.Add((root.Name.Length > 0 ? root.Name.TrimStart('#', '@') : root.Type, root));
                    }
                }
            }

            // "accent: auto" must become a real colour before the widgets resolve
            // $accent, so the surfaces and the WinForms overlays agree.
            NxTheme.ApplyAutoAccent(tokens, _autoAccent);

            _evaluator = new NxExpressionEvaluator(_registry, tokens);

            _windows.Clear();
            _loadedFiles.Clear();
            _loadedFiles.AddRange(files);

            foreach ((string name, NxNode node) in surfaces)
            {
                if (!IsEnabled(node, _evaluator))
                    continue;

                CreateSurface(name, node, _evaluator, animations);
            }

            Console.WriteLine(initial
                ? $"nx: loaded {files.Count} file(s), {_windows.Count} surface(s)"
                : $"nx: reloaded {files.Count} file(s), {_windows.Count} surface(s)");
        }

        private static bool IsEnabled(NxNode node, NxExpressionEvaluator evaluator)
        {
            NxProperty? enabled = node.FindProperty("enabled");

            return enabled == null || evaluator.EvaluateFlag(enabled.Value, true);
        }

        private void CreateSurface(
            string name,
            NxNode node,
            NxExpressionEvaluator evaluator,
            IReadOnlyDictionary<string, NxAnimation> animations)
        {
            try
            {
                NxWidget root = NxWidgetFactory.Build(node, evaluator, animations, null, _nowMs);
                var placement = NxSurfacePlacement.FromNode(node, evaluator);
                var context = new NxContext(_registry, evaluator, this);
                context.NowMs = _nowMs;

                var window = new NxSurfaceWindow(name, root, context, placement);
                window.ApplyPlacement(MonitorFor(window));
                window.Show();

                _windows.Add(window);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"nx: surface '{name}' failed: {ex}");
            }
        }

        private NxDocument? LoadDocument(string path)
        {
            try
            {
                string source = File.ReadAllText(path);
                return NxParser.Parse(source, path);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"nx: {Path.GetFileName(path)}: {ex.Message}");

                // A broken theme.nx falls back to the copy compiled into the
                // exe, so the interface keeps its palette instead of losing the
                // tokens every other document references.
                if (_fallbackTheme != null &&
                    string.Equals(Path.GetFileName(path), "theme.nx", StringComparison.OrdinalIgnoreCase))
                {
                    try
                    {
                        return NxParser.Parse(_fallbackTheme, "theme.nx (embedded)");
                    }
                    catch (Exception fallbackEx)
                    {
                        Console.WriteLine($"nx: embedded theme failed: {fallbackEx.Message}");
                    }
                }

                return null;
            }
        }

        private void WatchDirectories()
        {
            foreach (string directory in _directories)
            {
                if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
                    continue;

                try
                {
                    var watcher = new FileSystemWatcher(directory, "*.nx")
                    {
                        IncludeSubdirectories = true,
                        NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.Size,
                        EnableRaisingEvents = true
                    };

                    watcher.Changed += OnFileChanged;
                    watcher.Created += OnFileChanged;
                    watcher.Deleted += OnFileChanged;
                    watcher.Renamed += OnFileChanged;

                    _watchers.Add(watcher);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"NxRuntime: cannot watch '{directory}': {ex.Message}");
                }
            }
        }

        private void OnFileChanged(object sender, FileSystemEventArgs e)
        {
            // The reload itself runs on the UI thread; the watcher only raises a flag.
            Interlocked.Exchange(ref _reloadPending, 1);
            UiAnimationHost.RequestActivity();
        }

        /// <summary>Rebuilds every surface from disk.</summary>
        internal void Reload()
        {
            if (_disposed)
                return;

            foreach (NxSurfaceWindow window in _windows)
            {
                try
                {
                    window.Hide();
                    window.Dispose();
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"NxRuntime: surface close failed: {ex.Message}");
                }
            }

            _windows.Clear();
            LoadDocuments(initial: false);
        }

        // ----------------------------------------------------------------- actions

        /// <summary>
        /// Runs the action a button declared. Unknown actions are ignored so a
        /// document written for a newer build still loads.
        /// </summary>
        internal void RunAction(string action)
        {
            if (string.IsNullOrWhiteSpace(action))
                return;

            string trimmed = action.Trim();
            int separator = trimmed.IndexOf(':');

            string verb = separator > 0 ? trimmed[..separator] : trimmed;
            string argument = separator > 0 ? trimmed[(separator + 1)..].Trim() : string.Empty;

            try
            {
                switch (verb.ToLowerInvariant())
                {
                    case "launcher":
                        LauncherService.Toggle();
                        break;

                    case "rofi":
                    case "menu":
                        RofiMenuService.Toggle();
                        break;

                    case "settings":
                        SettingsWindowService.Show();
                        break;

                    case "terminal":
                        TerminalWindowService.OpenNew();
                        break;

                    case "hide":
                        LauncherService.Hide();
                        break;

                    case "reload":
                        Reload();
                        break;

                    case "exit":
                        Application.Exit();
                        break;

                    case "command":
                    case "run":
                        RunCommand(argument);
                        break;

                    default:
                        Console.WriteLine($"nx: unknown action '{verb}'");
                        break;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"nx: action '{action}' failed: {ex.Message}");
            }
        }

        private static void RunCommand(string command)
        {
            if (string.IsNullOrWhiteSpace(command))
                return;

            var startInfo = new ProcessStartInfo
            {
                FileName = command,
                UseShellExecute = true
            };

            Process.Start(startInfo);
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;
            Stop();
            _registry.Dispose();

            if (ReferenceEquals(Current, this))
                Current = null;
        }
    }
}
