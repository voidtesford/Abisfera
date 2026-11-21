using System.Diagnostics;
using Abisfera.AnimEditor.Model;
using Abisfera.AnimEditor.Rendering;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Content.Shared._CorvaxGoob.ProcAnim;
using SkiaSharp;
using AColor = Avalonia.Media.Color;
using RenderOptions = Abisfera.AnimEditor.Rendering.RenderOptions;

namespace Abisfera.AnimEditor.UI;

public sealed partial class MainWindow : Window
{
    private readonly Settings _settings = Settings.Load();
    private readonly string? _repoArg;
    private readonly string? _sceneArg;

    private EditorState _s = default!;
    private EditorCommands _cmd = default!;
    private Viewport _viewport = default!;
    private TimelineView _timeline = default!;
    private TextBlock _status = default!;
    private TreeView _sceneTree = default!;
    private TextBox _search = default!;
    private Button _playButton = default!;
    private TextBlock _timeText = default!;
    private readonly ComboBox[] _species = new ComboBox[2];
    private readonly ComboBox[] _outfits = new ComboBox[2];
    private readonly TextBlock[] _puppetLabels = new TextBlock[2];
    private ComboBox _editTarget = default!;
    private ComboBox _mode = default!;
    private ToggleButton _rotateTool = default!, _moveTool = default!;
    private ToggleButton _limits = default!, _onion = default!, _bones = default!, _effects = default!, _procedural = default!, _loop = default!;
    private List<SpeciesInfo> _speciesList = new();
    private DispatcherTimer? _timer;
    private readonly Stopwatch _clock = new();
    private bool _syncing;

    public MainWindow() : this(null, null)
    {
    }

    public MainWindow(string? repoArg, string? sceneArg)
    {
        _repoArg = repoArg;
        _sceneArg = sceneArg;
        Title = "Abisfera — редактор анимаций";
        Background = new SolidColorBrush(AColor.FromRgb(30, 32, 36));
        Width = _settings.Width;
        Height = _settings.Height;
        MinWidth = 1000;
        MinHeight = 640;
        try
        {
            Icon = new WindowIcon(Avalonia.Platform.AssetLoader.Open(new Uri("avares://AbisferaAnimEditor/Assets/icon.png")));
        }
        catch (Exception)
        {
            // no icon: not worth failing over
        }

        Content = Centered("Загрузка…");
        Opened += async (_, _) => await Startup();
        Closing += OnClosing;
    }

    private static Control Centered(string text) => new TextBlock
    {
        Text = text,
        FontSize = 18,
        HorizontalAlignment = HorizontalAlignment.Center,
        VerticalAlignment = VerticalAlignment.Center,
    };

    // ---------------- startup ----------------

    private static IEnumerable<string> AutoDetectCandidates()
    {
        foreach (var start in new[] { AppContext.BaseDirectory, Environment.CurrentDirectory })
        {
            var dir = new DirectoryInfo(start);
            for (var i = 0; dir != null && i < 8; i++, dir = dir.Parent)
                yield return dir.FullName;
        }
    }

    private async Task Startup()
    {
        var candidates = new List<string?> { _repoArg, _settings.Resources };
        candidates.AddRange(AutoDetectCandidates());
        foreach (var c in candidates)
        {
            if (string.IsNullOrWhiteSpace(c) || !Directory.Exists(c))
                continue;
            if (GameRepo.ResolveResources(c) is { } res)
            {
                await LoadRepo(res);
                return;
            }
        }

        ShowWelcome(null);
    }

    private void ShowWelcome(string? error)
    {
        var pick = new Button { Content = "Выбрать папку репозитория…", HorizontalAlignment = HorizontalAlignment.Center, Padding = new Thickness(18, 8) };
        pick.Click += async (_, _) => await PickRepo();
        var pathBox = new TextBox { Watermark = "…или вставьте путь, например C:\\Games\\abisfera", Width = 460 };
        var open = new Button { Content = "Открыть" };
        async Task OpenTyped()
        {
            var typed = pathBox.Text?.Trim().Trim('"') ?? "";
            if (typed.Length > 0 && GameRepo.ResolveResources(typed) is { } typedRes)
                await LoadRepo(typedRes);
            else
                ShowWelcome($"В «{typed}» нет папки Resources/Prototypes. Укажите корень репозитория игры.");
        }

        open.Click += async (_, _) => await OpenTyped();
        pathBox.KeyDown += async (_, e) =>
        {
            if (e.Key == Key.Enter)
                await OpenTyped();
        };
        var typedRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, HorizontalAlignment = HorizontalAlignment.Center, Children = { pathBox, open } };
        var panel = new StackPanel
        {
            Spacing = 14,
            MaxWidth = 620,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Children =
            {
                new TextBlock { Text = "Редактор анимаций Abisfera", FontSize = 26, FontWeight = FontWeight.SemiBold, HorizontalAlignment = HorizontalAlignment.Center },
                new TextBlock
                {
                    Text = "Укажите папку с репозиторием игры (или саму папку Resources внутри него). " +
                           "Редактор читает оттуда спрайты рас и анимации и сохраняет туда же изменения. Папка запоминается.",
                    TextWrapping = TextWrapping.Wrap,
                    TextAlignment = TextAlignment.Center,
                    Opacity = 0.85,
                },
                pick,
                typedRow,
            },
        };
        if (error != null)
            panel.Children.Add(new TextBlock { Text = error, Foreground = Brushes.OrangeRed, TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center });
        Content = panel;
    }

    private async Task PickRepo()
    {
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Папка репозитория игры (или Resources)",
            AllowMultiple = false,
        });
        var path = folders.FirstOrDefault()?.TryGetLocalPath();
        if (path == null)
            return;
        var res = GameRepo.ResolveResources(path);
        if (res == null)
        {
            if (_s == null)
                ShowWelcome($"В «{path}» нет папки Resources/Prototypes с анимациями ProcAnim. Выберите корень репозитория.");
            else
                await Dialogs.Info(this, "Не та папка", $"В «{path}» нет папки Resources/Prototypes с анимациями ProcAnim.");
            return;
        }

        await LoadRepo(res);
    }

    private async Task LoadRepo(string res)
    {
        Content = Centered($"Загрузка игровых данных…\n{res}");
        GameRepo repo;
        try
        {
            repo = await Task.Run(() => GameRepo.Load(res));
        }
        catch (Exception e)
        {
            Program.CrashLog(e);
            ShowWelcome($"Не удалось прочитать {res}: {e.Message}");
            return;
        }

        _settings.Resources = res;
        _settings.Save();
        _s = new EditorState
        {
            Repo = repo,
            Renderer = new SceneRenderer(repo, new RsiLoader(repo.Resources)),
            Settings = _settings,
        };
        _cmd = new EditorCommands(_s);
        BuildEditor();

        var scene = repo.Scenes.FirstOrDefault(x => x.Id == _sceneArg)
                    ?? repo.Scenes.FirstOrDefault(x => x.Id == _settings.LastScene && InMode(x))
                    ?? repo.Scenes.FirstOrDefault(InMode)
                    ?? repo.Scenes.FirstOrDefault();
        if (scene != null)
            OpenScene(scene);
        if (repo.LoadErrors.Count > 0)
            SetStatus($"Загружено с предупреждениями ({repo.LoadErrors.Count}): {repo.LoadErrors[0]}");
    }

    private bool InMode(SceneInfo s) => _settings.Mode != "Finisher" || s.Category == nameof(ProcAnimCategory.Finisher);

    // ---------------- layout ----------------

    private void BuildEditor()
    {
        _viewport = new Viewport(_s);
        _timeline = new TimelineView(_s, _cmd);
        _status = new TextBlock { Margin = new Thickness(8, 3), Opacity = 0.85, TextTrimming = TextTrimming.CharacterEllipsis };
        _s.Status += SetStatus;
        _s.DocChanged += OnDocChanged;
        _s.TimeChanged += OnTimeChanged;
        _s.SelectionChanged += OnSelectionChanged;

        var grid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("270,4,*,4,340"),
            RowDefinitions = new RowDefinitions("*,4,250"),
        };
        var left = BuildSceneList();
        Grid.SetRowSpan(left, 3);
        grid.Children.Add(left);
        grid.Children.Add(Cell(new GridSplitter { ResizeDirection = GridResizeDirection.Columns, Background = Brushes.Black }, 0, 1, 3));
        grid.Children.Add(Cell(new Border { Child = _viewport, Background = new SolidColorBrush(AColor.FromRgb(46, 48, 54)) }, 0, 2));
        grid.Children.Add(Cell(new GridSplitter { ResizeDirection = GridResizeDirection.Columns, Background = Brushes.Black }, 0, 3));
        grid.Children.Add(Cell(BuildSidePanel(), 0, 4));
        grid.Children.Add(Cell(new GridSplitter { ResizeDirection = GridResizeDirection.Rows, Background = Brushes.Black }, 1, 2, 1, 3));
        var bottom = new DockPanel();
        var transport = BuildTransport();
        DockPanel.SetDock(transport, Dock.Top);
        bottom.Children.Add(transport);
        bottom.Children.Add(_timeline);
        grid.Children.Add(Cell(bottom, 2, 2, 1, 3));

        var root = new DockPanel();
        var menu = BuildMenu();
        DockPanel.SetDock(menu, Dock.Top);
        root.Children.Add(menu);
        var toolbar = BuildToolbar();
        DockPanel.SetDock(toolbar, Dock.Top);
        root.Children.Add(toolbar);
        var status = new Border { Child = _status, Background = new SolidColorBrush(AColor.FromRgb(28, 30, 34)) };
        DockPanel.SetDock(status, Dock.Bottom);
        root.Children.Add(status);
        root.Children.Add(grid);
        Content = root;

        _timer = new DispatcherTimer(TimeSpan.FromMilliseconds(15), DispatcherPriority.Render, OnTick);
        _timer.Start();
        _clock.Start();
        AddHandler(KeyDownEvent, OnKey, Avalonia.Interactivity.RoutingStrategies.Tunnel);
        SetStatus($"Папка игры: {_s.Repo.Resources} · рас: {_s.Repo.Species.Count} · анимаций: {_s.Repo.Scenes.Count}");
    }

    private static Control Cell(Control c, int row, int col, int rowSpan = 1, int colSpan = 1)
    {
        Grid.SetRow(c, row);
        Grid.SetColumn(c, col);
        Grid.SetRowSpan(c, rowSpan);
        Grid.SetColumnSpan(c, colSpan);
        return c;
    }

    private Menu BuildMenu()
    {
        MenuItem Item(string header, Action act, string? gesture = null, Func<bool>? enabled = null)
        {
            var mi = new MenuItem { Header = header };
            if (gesture != null)
                mi.InputGesture = KeyGesture.Parse(gesture);
            mi.Click += (_, _) => act();
            return mi;
        }

        MenuItem Async(string header, Func<Task> act, string? gesture = null)
            => Item(header, () => _ = act(), gesture);

        var file = new MenuItem
        {
            Header = "_Файл",
            ItemsSource = new object[]
            {
                Async("Новая анимация добивания…", NewFinisher, "Ctrl+N"),
                new Separator(),
                Async("Сохранить", () => Save(), "Ctrl+S"),
                Async("Сохранить как копию…", SaveAsCopy, "Ctrl+Shift+S"),
                Async("Экспорт кадра в PNG…", ExportPng, "Ctrl+E"),
                new Separator(),
                Item("Перечитать анимации с диска", () => { _s.Repo.RescanScenes(); RefreshSceneList(); SetStatus("Список анимаций обновлён"); }, "F5"),
                Async("Сменить папку игры…", PickRepo),
                Item("Открыть папку файла сцены", OpenSceneFolder),
                new Separator(),
                Item("Выход", Close, "Alt+F4"),
            },
        };
        var edit = new MenuItem
        {
            Header = "_Правка",
            ItemsSource = new object[]
            {
                Item("Отменить", _cmd.Undo, "Ctrl+Z"),
                Item("Повторить", _cmd.Redo, "Ctrl+Y"),
                new Separator(),
                Item("Копировать позу", _cmd.Copy, "Ctrl+C"),
                Item("Вставить позу", _cmd.Paste, "Ctrl+V"),
                new Separator(),
                Item("Инструмент: вращение", () => SetTool(Tool.Rotate), "R"),
                Item("Инструмент: смещение", () => SetTool(Tool.Translate), "G"),
                Item("Сбросить выбранную кость в 0", _cmd.ResetSelected, "Ctrl+0"),
                Item("Удалить ключ выбранной кости", _cmd.DeleteSelectedKey, "Shift+Delete"),
            },
        };
        var frame = new MenuItem
        {
            Header = "_Кадр",
            ItemsSource = new object[]
            {
                Item("Записать позу (новый кадр)", _cmd.InsertFrame, "K"),
                Item("Дублировать кадр", _cmd.DuplicateFrame, "Ctrl+D"),
                Item("Удалить кадр", _cmd.DeleteFrame, "Delete"),
                new Separator(),
                Item("Предыдущий кадр", _cmd.PrevFrame, "Left"),
                Item("Следующий кадр", _cmd.NextFrame, "Right"),
                Item("Назад на 0.01 с", () => _cmd.Step(-0.01f), "OemComma"),
                Item("Вперёд на 0.01 с", () => _cmd.Step(0.01f), "OemPeriod"),
                Item("В начало", () => _s.SetTime(_s.Start), "Home"),
                Item("В конец", () => _s.SetTime(_s.End), "End"),
                new Separator(),
                Item("Воспроизвести / пауза", TogglePlay, "Space"),
            },
        };
        var view = new MenuItem
        {
            Header = "_Вид",
            ItemsSource = new object[]
            {
                Item("Показать всё", () => { _viewport.Fit(); _timeline.FitTime(); }, "F"),
                Item("Приблизить", () => _viewport.ZoomBy(1.25f), "OemPlus"),
                Item("Отдалить", () => _viewport.ZoomBy(0.8f), "OemMinus"),
                new Separator(),
                Item("Калька (соседние кадры)", () => _onion.IsChecked = !(_onion.IsChecked ?? false), "O"),
                Item("Кости", () => _bones.IsChecked = !(_bones.IsChecked ?? false), "B"),
                Item("Ограничения суставов", () => _limits.IsChecked = !(_limits.IsChecked ?? false), "J"),
                Item("Повтор воспроизведения", () => _loop.IsChecked = !(_loop.IsChecked ?? false), "L"),
                new Separator(),
                Item("Править куклу 1", () => _editTarget.SelectedIndex = 0, "D1"),
                Item("Править куклу 2", () => _editTarget.SelectedIndex = 1, "D2"),
                Item("Править обе куклы", () => _editTarget.SelectedIndex = 2, "D3"),
                Item("Переключить куклу", () => _editTarget.SelectedIndex = (_editTarget.SelectedIndex + 1) % 3, "Tab"),
            },
        };
        var help = new MenuItem
        {
            Header = "_Справка",
            ItemsSource = new object[] { Async("Управление…", ShowHelp, "F1") },
        };
        return new Menu { ItemsSource = new[] { file, edit, frame, view, help } };
    }

    private Control BuildToolbar()
    {
        var bar = new WrapPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(6, 4) };

        _mode = new ComboBox { ItemsSource = new[] { "Добивания", "Все анимации" }, SelectedIndex = _settings.Mode == "Finisher" ? 0 : 1, Width = 150 };
        _mode.SelectionChanged += (_, _) =>
        {
            _settings.Mode = _mode.SelectedIndex == 0 ? "Finisher" : "All";
            _settings.Save();
            RefreshSceneList();
        };
        bar.Children.Add(Group("Режим", _mode));

        _speciesList = _s.Repo.Species.Values.OrderBy(x => x.Id == "Human" ? 0 : 1).ThenBy(x => x.Name).ToList();
        var speciesNames = _speciesList.Select(x => _speciesList.Count(y => y.Name == x.Name) > 1 ? $"{x.Name} ({x.Id})" : x.Name).ToList();
        for (var i = 0; i < 2; i++)
        {
            var idx = i;
            var app = _settings.Puppet(i);
            _puppetLabels[i] = new TextBlock { Text = $"Кукла {i + 1}", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0), FontWeight = FontWeight.SemiBold };
            _species[i] = new ComboBox { ItemsSource = speciesNames, Width = 150, SelectedIndex = Math.Max(0, _speciesList.FindIndex(x => x.Id == app.Species)) };
            ToolTip.SetTip(_species[i], "Раса куклы (спрайты берутся из игры)");
            _outfits[i] = new ComboBox { ItemsSource = new[] { "Без одежды", "Комбинезон", "Броня Гилберта" }, Width = 140, SelectedIndex = (int) app.Outfit };
            _species[i].SelectionChanged += (_, _) => PuppetChanged(idx);
            _outfits[i].SelectionChanged += (_, _) => PuppetChanged(idx);
            bar.Children.Add(new Border
            {
                Margin = new Thickness(4, 0),
                Padding = new Thickness(6, 2),
                CornerRadius = new CornerRadius(4),
                Background = new SolidColorBrush(i == 0 ? AColor.FromArgb(50, 255, 120, 80) : AColor.FromArgb(50, 80, 160, 255)),
                Child = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, Children = { _puppetLabels[i], _species[i], _outfits[i] } },
            });
        }

        _editTarget = new ComboBox { ItemsSource = new[] { "Кукла 1", "Кукла 2", "Обе" }, SelectedIndex = 0, Width = 110 };
        _editTarget.SelectionChanged += (_, _) => _s.SetEditActor(_editTarget.SelectedIndex == 2 ? -1 : _editTarget.SelectedIndex);
        ToolTip.SetTip(_editTarget, "Чьи кости и кадры правим (1 / 2 / 3, Tab)");
        bar.Children.Add(Group("Правим", _editTarget));

        _rotateTool = new ToggleButton { Content = "⟳ Вращать (R)", IsChecked = true };
        _moveTool = new ToggleButton { Content = "✥ Двигать (G)" };
        _rotateTool.Click += (_, _) => SetTool(Tool.Rotate);
        _moveTool.Click += (_, _) => SetTool(Tool.Translate);
        ToolTip.SetTip(_rotateTool, "Тяните часть тела — она вращается вокруг сустава. Ctrl — шаг 15°. Shift — временно другой инструмент.");
        ToolTip.SetTip(_moveTool, "Тяните часть тела — она смещается (в пикселях). Ctrl — без округления.");
        bar.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2, Margin = new Thickness(8, 0), Children = { _rotateTool, _moveTool } });

        ToggleButton Toggle(string text, bool value, string tip, Action<bool> set)
        {
            var t = new ToggleButton { Content = text, IsChecked = value, Margin = new Thickness(2, 0) };
            ToolTip.SetTip(t, tip);
            t.IsCheckedChanged += (_, _) =>
            {
                set(t.IsChecked == true);
                _settings.Save();
                _viewport?.InvalidateVisual();
            };
            return t;
        }

        _limits = Toggle("Суставы", _settings.Limits, "Мягкие ограничения суставов при вращении (J)", v => _settings.Limits = v);
        _onion = Toggle("Калька", _settings.Onion, "Полупрозрачные соседние кадры: красный — предыдущий, зелёный — следующий (O)", v => _settings.Onion = v);
        _bones = Toggle("Кости", _settings.Bones, "Показывать суставы и кости (B)", v => _settings.Bones = v);
        _effects = Toggle("Эффекты", _settings.Effects, "Кровь, вспышки, звёзды, синяки — как в игре", v => _settings.Effects = v);
        _procedural = Toggle("Процедурка", _settings.Procedural, "Процедурные слои игры (дыхание, IK, отдача, взгляд). Выключите, чтобы видеть только ключи.", v => _settings.Procedural = v);
        bar.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Children = { _limits, _onion, _bones, _effects, _procedural } });
        return new Border { Child = bar, Background = new SolidColorBrush(AColor.FromRgb(36, 38, 43)) };
    }

    private static Control Group(string label, Control c) => new StackPanel
    {
        Orientation = Orientation.Horizontal,
        Spacing = 6,
        Margin = new Thickness(4, 0, 8, 0),
        Children = { new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center, Opacity = 0.8 }, c },
    };

    private void PuppetChanged(int i)
    {
        if (_syncing || _species[i].SelectedIndex < 0)
            return;
        var sp = _speciesList[_species[i].SelectedIndex];
        var app = _settings.Puppet(i) with { Species = sp.Id, Outfit = (Outfit) Math.Max(0, _outfits[i].SelectedIndex), Skin = null };
        _s.SetApp(i, app);
        SetStatus($"Кукла {i + 1}: {sp.Name}");
    }

    private void SetTool(Tool tool)
    {
        _s.Tool = tool;
        _rotateTool.IsChecked = tool == Tool.Rotate;
        _moveTool.IsChecked = tool == Tool.Translate;
        _viewport.InvalidateVisual();
    }

    private Control BuildTransport()
    {
        Button B(string text, string tip, Action act)
        {
            var b = new Button { Content = text, Padding = new Thickness(10, 3), Margin = new Thickness(1, 0) };
            ToolTip.SetTip(b, tip);
            b.Click += (_, _) => act();
            return b;
        }

        _playButton = B("▶", "Воспроизвести / пауза (Пробел)", TogglePlay);
        _playButton.MinWidth = 44;
        _loop = new ToggleButton { Content = "Повтор", IsChecked = _settings.Loop, Margin = new Thickness(6, 0) };
        _loop.IsCheckedChanged += (_, _) =>
        {
            _settings.Loop = _loop.IsChecked == true;
            _settings.Save();
        };
        ToolTip.SetTip(_loop, "Зациклить воспроизведение (L)");
        var speed = new ComboBox { ItemsSource = new[] { "×0.25", "×0.5", "×1", "×2" }, SelectedIndex = 2, Width = 80 };
        speed.SelectionChanged += (_, _) => _s.Speed = new[] { 0.25f, 0.5f, 1f, 2f }[Math.Max(0, speed.SelectedIndex)];
        ToolTip.SetTip(speed, "Скорость воспроизведения");
        _timeText = new TextBlock { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0), FontFamily = new FontFamily("Consolas, DejaVu Sans Mono, monospace") };
        return new Border
        {
            Background = new SolidColorBrush(AColor.FromRgb(36, 38, 43)),
            Padding = new Thickness(4, 3),
            Child = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Children =
                {
                    B("⏮", "В начало (Home)", () => _s.SetTime(_s.Start)),
                    B("◀|", "Предыдущий кадр (←)", _cmd.PrevFrame),
                    _playButton,
                    B("|▶", "Следующий кадр (→)", _cmd.NextFrame),
                    B("⏭", "В конец (End)", () => _s.SetTime(_s.End)),
                    _loop,
                    speed,
                    _timeText,
                    B("+ Кадр (K)", "Записать текущую позу как кадр", _cmd.InsertFrame),
                    B("Дубль (Ctrl+D)", "Дублировать кадр, остальное сдвинуть", _cmd.DuplicateFrame),
                    B("Удалить (Del)", "Удалить кадр на курсоре", _cmd.DeleteFrame),
                },
            },
        };
    }

    // ---------------- scene list ----------------

    private Control BuildSceneList()
    {
        _search = new TextBox { Watermark = "Поиск анимации…", Margin = new Thickness(6) };
        _search.TextChanged += (_, _) => RefreshSceneList();
        _sceneTree = new TreeView { Margin = new Thickness(2, 0) };
        _sceneTree.SelectionChanged += OnSceneSelected;
        var newButton = new Button { Content = "+ Новое добивание…", HorizontalAlignment = HorizontalAlignment.Stretch, Margin = new Thickness(6) };
        newButton.Click += async (_, _) => await NewFinisher();
        var dock = new DockPanel();
        DockPanel.SetDock(_search, Dock.Top);
        DockPanel.SetDock(newButton, Dock.Bottom);
        dock.Children.Add(_search);
        dock.Children.Add(newButton);
        dock.Children.Add(_sceneTree);
        RefreshSceneList();
        return new Border { Child = dock, Background = new SolidColorBrush(AColor.FromRgb(36, 38, 43)) };
    }

    private static string CategoryName(string c) => c switch
    {
        nameof(ProcAnimCategory.Finisher) => "Добивания",
        nameof(ProcAnimCategory.Ability) => "Способности",
        nameof(ProcAnimCategory.Example) => "Примеры",
        _ => "Прочее",
    };

    private void RefreshSceneList()
    {
        if (_sceneTree == null)
            return;
        _syncing = true;
        var filter = _search.Text?.Trim() ?? "";
        var groups = _s.Repo.Scenes
            .Where(InMode)
            .Where(x => filter.Length == 0 || x.Title.Contains(filter, StringComparison.OrdinalIgnoreCase))
            .GroupBy(x => x.Category)
            .OrderBy(g => g.Key == nameof(ProcAnimCategory.Finisher) ? 0 : g.Key == nameof(ProcAnimCategory.Other) ? 9 : 1);
        var items = new List<TreeViewItem>();
        TreeViewItem? current = null;
        foreach (var g in groups)
        {
            var children = new List<TreeViewItem>();
            foreach (var sc in g.OrderBy(x => x.Title))
            {
                var tvi = new TreeViewItem
                {
                    Header = new StackPanel
                    {
                        Children =
                        {
                            new TextBlock { Text = sc.EditorName ?? sc.Id, TextTrimming = TextTrimming.CharacterEllipsis },
                            new TextBlock { Text = $"{sc.Id} · {sc.Length:0.##} с · {string.Join(", ", sc.Roles)}", FontSize = 11, Opacity = 0.6, TextTrimming = TextTrimming.CharacterEllipsis },
                        },
                    },
                    Tag = sc,
                };
                if (_s.Doc?.Id == sc.Id)
                    current = tvi;
                children.Add(tvi);
            }

            items.Add(new TreeViewItem { Header = $"{CategoryName(g.Key)} ({children.Count})", ItemsSource = children, IsExpanded = true, FontWeight = FontWeight.SemiBold });
        }

        _sceneTree.ItemsSource = items;
        if (current != null)
            _sceneTree.SelectedItem = current;
        _syncing = false;
    }

    private async void OnSceneSelected(object? sender, SelectionChangedEventArgs e)
    {
        if (_syncing || _sceneTree.SelectedItem is not TreeViewItem { Tag: SceneInfo info } || info.Id == _s.Doc?.Id)
            return;
        if (!await ConfirmDiscard())
        {
            RefreshSceneList();
            return;
        }

        OpenScene(info);
    }

    /// <summary>True when it is fine to drop the open scene (saved, or the user chose).</summary>
    private async Task<bool> ConfirmDiscard()
    {
        if (_s.Doc is not { Dirty: true } doc)
            return true;
        var r = await Dialogs.Ask(this, "Несохранённые изменения", $"В «{doc.Id}» есть несохранённые изменения.", "Сохранить", "Не сохранять", "Отмена");
        return r switch
        {
            0 => await Save(),
            1 => true,
            _ => false,
        };
    }

    private void OpenScene(SceneInfo info)
    {
        try
        {
            var doc = SceneDocument.Load(info.File, info.Id);
            _s.Open(doc);
            _viewport.Fit();
            _timeline.FitTime();
            RefreshSceneList();
            SetStatus($"Открыто: {info.Id} — {RelPath(info.File)}" + (doc.Problems.Count > 0 ? $" · проблем: {doc.Problems.Count} (вкладка «Сцена»)" : ""));
        }
        catch (Exception e)
        {
            Program.CrashLog(e);
            _ = Dialogs.Info(this, "Не удалось открыть", $"{info.Id}: {e.Message}");
        }
    }

    private string RelPath(string file) => Path.GetRelativePath(Path.GetDirectoryName(_s.Repo.Resources) ?? _s.Repo.Resources, file);

    // ---------------- file operations ----------------

    private async Task<bool> Save()
    {
        if (_s.Doc is not { } doc)
            return false;
        if (doc.Problems.Count > 0)
        {
            var r = await Dialogs.Ask(this, "В сцене есть проблемы",
                "Игра может не загрузить или неверно проиграть сцену:\n\n• " + string.Join("\n• ", doc.Problems.Take(20)) + "\n\nВсё равно сохранить?",
                "Сохранить", "Отмена");
            if (r != 0)
                return false;
        }

        try
        {
            var path = doc.Save();
            _s.Repo.RescanScenes();
            RefreshSceneList();
            SetStatus($"Сохранено: {RelPath(path)} ({DateTime.Now:HH:mm:ss})");
            _s.RaiseDocChanged();
            return true;
        }
        catch (Exception e)
        {
            Program.CrashLog(e);
            await Dialogs.Info(this, "Ошибка сохранения", e.Message);
            return false;
        }
    }

    private string EditorFile(string id) => Path.Combine(_s.Repo.Prototypes, Templates.EditorFolder.Replace('/', Path.DirectorySeparatorChar), id + ".yml");

    private async Task<(string Id, string Name)?> AskNewId(string title, string id, string name)
    {
        while (true)
        {
            var r = await Dialogs.Prompt(this, title,
                $"Сцена сохранится в Resources/Prototypes/{Templates.EditorFolder}/<id>.yml — игра подхватит её при следующей загрузке прототипов.",
                ("ID прототипа (латиница, цифры, _)", id), ("Название в редакторе", name));
            if (r == null)
                return null;
            id = r[0].Trim();
            name = r[1].Trim();
            if (!Templates.ValidId(id))
            {
                await Dialogs.Info(this, "Неверный ID", "ID должен начинаться с буквы и состоять из латиницы, цифр и «_».");
                continue;
            }

            if (_s.Repo.Scenes.Any(x => x.Id == id))
            {
                await Dialogs.Info(this, "ID занят", $"Сцена «{id}» уже есть.");
                continue;
            }

            return (id, name);
        }
    }

    private async Task NewFinisher()
    {
        if (!await ConfirmDiscard())
            return;
        var r = await AskNewId("Новая анимация добивания", "MyFinisher", "Моё добивание");
        if (r is not { } v)
            return;
        var doc = SceneDocument.FromNode(EditorFile(v.Id), v.Id, Templates.NewFinisher(v.Id, v.Name.Length > 0 ? v.Name : v.Id));
        _s.Open(doc);
        _viewport.Fit();
        _timeline.FitTime();
        SetStatus($"Новая сцена {v.Id} (ещё не сохранена: Ctrl+S)");
    }

    private async Task SaveAsCopy()
    {
        if (_s.Doc is not { } doc)
            return;
        var r = await AskNewId("Сохранить как копию", doc.Id + "Copy", (doc.Node.Str("editorName") ?? doc.Id) + " (копия)");
        if (r is not { } v)
            return;
        try
        {
            var copy = doc.SaveAsNew(EditorFile(v.Id), v.Id);
            if (v.Name.Length > 0)
            {
                copy.Edit(() => copy.Node.Set("editorName", YamlText.S(v.Name)));
                copy.Save();
            }

            _s.Repo.RescanScenes();
            _s.Open(copy);
            RefreshSceneList();
            SetStatus($"Копия сохранена: {RelPath(copy.FilePath)}");
        }
        catch (Exception e)
        {
            await Dialogs.Info(this, "Ошибка сохранения", e.Message);
        }
    }

    private async Task ExportPng()
    {
        if (_s.Doc is not { } doc)
            return;
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Экспорт кадра",
            SuggestedFileName = $"{doc.Id}_{_s.Time:0.000}.png".Replace(',', '.'),
            FileTypeChoices = new[] { new FilePickerFileType("PNG") { Patterns = new[] { "*.png" } } },
        });
        var path = file?.TryGetLocalPath();
        if (path == null)
            return;
        RenderPng(path, 4f);
        SetStatus($"Кадр сохранён: {path}");
    }

    /// <summary>Clean render of the current frame (no overlay), framed on both actors.</summary>
    public void RenderPng(string path, float scale)
    {
        var doc = _s.Doc!;
        var frames = _s.Renderer.Frames;
        var xs = frames.Select(f => f.Entity.X).DefaultIfEmpty(0).ToList();
        var ys = frames.Select(f => f.Entity.Y).DefaultIfEmpty(0).ToList();
        var cx = (xs.Min() + xs.Max()) / 2;
        var cy = (ys.Min() + ys.Max()) / 2;
        var w = (int) ((xs.Max() - xs.Min() + 112) * scale);
        var h = (int) ((ys.Max() - ys.Min() + 96) * scale);
        using var bmp = new SKBitmap(w, h);
        using var canvas = new SKCanvas(bmp);
        canvas.Clear(SKColors.Transparent);
        var cam = Affine.Translate(w / 2f, h / 2f) * Affine.Scale(scale, -scale) * Affine.Translate(-cx, -cy);
        _s.Renderer.Render(canvas, doc.Proto, _s.App, _s.Time, _s.Real, cam, new RenderOptions { CollectHits = false });
        using var img = SKImage.FromBitmap(bmp);
        using var data = img.Encode(SKEncodedImageFormat.Png, 100);
        File.WriteAllBytes(path, data.ToArray());
        // the renderer's per-frame state now describes the export camera: redraw the view
        _viewport.InvalidateVisual();
    }

    private void OpenSceneFolder()
    {
        if (_s.Doc is not { } doc)
            return;
        try
        {
            var dir = Path.GetDirectoryName(doc.FilePath)!;
            Directory.CreateDirectory(dir);
            Process.Start(new ProcessStartInfo { FileName = dir, UseShellExecute = true });
        }
        catch (Exception e)
        {
            SetStatus("Не удалось открыть папку: " + e.Message);
        }
    }

    private async void OnClosing(object? sender, WindowClosingEventArgs e)
    {
        _settings.Width = Width;
        _settings.Height = Height;
        _settings.Save();
        if (_s?.Doc is not { Dirty: true } || _closeConfirmed)
            return;
        e.Cancel = true;
        if (await ConfirmDiscard())
        {
            _closeConfirmed = true;
            Close();
        }
    }

    private bool _closeConfirmed;

    private Task ShowHelp() => Dialogs.Info(this, "Управление", HelpText);

    public const string HelpText = """
Сцена (окно просмотра)
  ЛКМ по части тела — выбрать и тянуть: вращение вокруг сустава (R) или смещение (G).
  Shift при перетаскивании — временно другой инструмент. Ctrl — шаг 15° / без округления пикселей.
  Оранжевый прямоугольник под ногами — корень куклы: тяните, чтобы двигать её по полу (ключи move).
  Голубой крестик — цель IK у конечности, которую ведёт IK: тяните, чтобы сдвинуть цель.
  ПКМ / СКМ — двигать вид, колесо — масштаб, F — показать всё. Esc — отменить текущее перетаскивание.
  Любое изменение позы сразу записывает ключ в текущее время (кадр создаётся автоматически).

Таймлайн
  Клик — перемотка (прилипает к кадрам, Ctrl — без прилипания). Тяните ромб кадра/ключа — перенос во времени.
  ПКМ по кадру/ключу — плавность, дублирование, удаление. Ctrl+колесо — масштаб времени, Shift+колесо — прокрутка.
  Цифры между кадрами — длительность кадра; поменять её можно во вкладке «Кадр».

Клавиши
  Пробел — играть/пауза; ← / → — предыдущий/следующий кадр; , / . — шаг 0.01 с; Home / End.
  K — записать позу (кадр); Ctrl+D — дублировать кадр; Del — удалить кадр; Shift+Del — удалить ключ кости.
  Ctrl+C / Ctrl+V — копировать/вставить позу; Ctrl+Z / Ctrl+Y — отмена/повтор; Ctrl+S — сохранить.
  1 / 2 / 3 / Tab — какую куклу правим; R / G — инструмент; O — калька; B — кости; J — ограничения суставов; L — повтор.
""";

    // ---------------- keyboard / playback ----------------

    private void OnKey(object? sender, KeyEventArgs e)
    {
        if (_s?.Doc == null)
            return;
        if (FocusManager?.GetFocusedElement() is TextBox or NumericUpDown or AutoCompleteBox)
        {
            if (e.Key == Key.Escape)
                _viewport.Focus();
            if (!(e.KeyModifiers == KeyModifiers.Control && e.Key == Key.S))
                return;
        }

        var ctrl = e.KeyModifiers.HasFlag(KeyModifiers.Control);
        var shift = e.KeyModifiers.HasFlag(KeyModifiers.Shift);
        var handled = true;
        switch (e.Key)
        {
            case Key.Space: TogglePlay(); break;
            case Key.Left when !ctrl: _cmd.PrevFrame(); break;
            case Key.Right when !ctrl: _cmd.NextFrame(); break;
            case Key.OemComma: _cmd.Step(-0.01f); break;
            case Key.OemPeriod: _cmd.Step(0.01f); break;
            case Key.Home: _s.SetTime(_s.Start); break;
            case Key.End: _s.SetTime(_s.End); break;
            case Key.K or Key.Insert: _cmd.InsertFrame(); break;
            case Key.Delete when shift: _cmd.DeleteSelectedKey(); break;
            case Key.Delete: _cmd.DeleteFrame(); break;
            case Key.D when ctrl: _cmd.DuplicateFrame(); break;
            case Key.C when ctrl: _cmd.Copy(); break;
            case Key.V when ctrl: _cmd.Paste(); break;
            case Key.Z when ctrl && shift: _cmd.Redo(); break;
            case Key.Z when ctrl: _cmd.Undo(); break;
            case Key.Y when ctrl: _cmd.Redo(); break;
            case Key.S when ctrl && shift: _ = SaveAsCopy(); break;
            case Key.S when ctrl: _ = Save(); break;
            case Key.N when ctrl: _ = NewFinisher(); break;
            case Key.E when ctrl: _ = ExportPng(); break;
            case Key.D0 when ctrl: _cmd.ResetSelected(); break;
            case Key.R when !ctrl: SetTool(Tool.Rotate); break;
            case Key.G when !ctrl: SetTool(Tool.Translate); break;
            case Key.O: _onion.IsChecked = !(_onion.IsChecked ?? false); break;
            case Key.B: _bones.IsChecked = !(_bones.IsChecked ?? false); break;
            case Key.J: _limits.IsChecked = !(_limits.IsChecked ?? false); break;
            case Key.L: _loop.IsChecked = !(_loop.IsChecked ?? false); break;
            case Key.F: _viewport.Fit(); _timeline.FitTime(); break;
            case Key.D1 or Key.NumPad1: _editTarget.SelectedIndex = 0; break;
            case Key.D2 or Key.NumPad2: _editTarget.SelectedIndex = 1; break;
            case Key.D3 or Key.NumPad3: _editTarget.SelectedIndex = 2; break;
            case Key.Tab: _editTarget.SelectedIndex = (_editTarget.SelectedIndex + 1) % 3; break;
            case Key.OemPlus or Key.Add: _viewport.ZoomBy(1.25f); break;
            case Key.OemMinus or Key.Subtract: _viewport.ZoomBy(0.8f); break;
            case Key.F1: _ = ShowHelp(); break;
            case Key.F5: _s.Repo.RescanScenes(); RefreshSceneList(); break;
            case Key.Escape:
                if (!_viewport.CancelDrag())
                    _s.Select(null);
                break;
            default: handled = false; break;
        }

        e.Handled = handled;
    }

    private void TogglePlay()
    {
        if (_s.Doc == null)
            return;
        if (!_s.Playing && _s.Time >= _s.End - 0.001f)
            _s.SetTime(_s.Start);
        _s.Playing = !_s.Playing;
        _playButton.Content = _s.Playing ? "⏸" : "▶";
        _viewport.InvalidateVisual();
    }

    private void OnTick(object? sender, EventArgs e)
    {
        var dt = (float) _clock.Elapsed.TotalSeconds;
        _clock.Restart();
        if (!_s.Playing)
        {
            if ((string?) _playButton.Content != "▶")
                _playButton.Content = "▶";
            return;
        }

        _s.Advance(MathF.Min(dt, 0.1f));
    }

    private void SetStatus(string text)
    {
        if (_status != null)
            _status.Text = text;
    }
}
