using System.Globalization;
using Abisfera.AnimEditor.Model;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Content.Shared._CorvaxGoob.ProcAnim;

namespace Abisfera.AnimEditor.UI;

public sealed partial class MainWindow
{
    private TextBlock _boneTitle = default!, _boneKeyInfo = default!, _boneHint = default!;
    private ListBox _boneList = default!;
    private NumericUpDown _keyX = default!, _keyY = default!, _keyRot = default!;
    private TextBlock _keyXLabel = default!, _keyYLabel = default!;
    private ComboBox _keyEase = default!;
    private TextBlock _frameInfo = default!;
    private NumericUpDown _frameDuration = default!;
    private CheckBox _ripple = default!;
    private ComboBox _frameEase = default!;
    private TextBox _sceneName = default!;
    private ComboBox _sceneCategory = default!;
    private NumericUpDown _sceneLength = default!;
    private TextBlock _sceneInfo = default!, _problems = default!, _events = default!;
    private TextBox _yaml = default!;
    private TabControl _tabs = default!;
    private List<TrackRef> _boneTracks = new();
    private List<string> _boneLabels = new();
    private int _shownRevision = -1;

    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    private Control BuildSidePanel()
    {
        _tabs = new TabControl { Padding = new Thickness(4) };
        _tabs.ItemsSource = new[]
        {
            new TabItem { Header = "Кость", FontSize = 15, Content = BuildBoneTab() },
            new TabItem { Header = "Кадр", FontSize = 15, Content = new ScrollViewer { Content = BuildFrameTab() } },
            new TabItem { Header = "Сцена", FontSize = 15, Content = new ScrollViewer { Content = BuildSceneTab() } },
            new TabItem { Header = "YAML", FontSize = 15, Content = BuildYamlTab() },
        };
        _tabs.SelectionChanged += (_, _) => RefreshYaml(false);
        return new Border { Child = _tabs, Background = new SolidColorBrush(Avalonia.Media.Color.FromRgb(36, 38, 43)) };
    }

    private static NumericUpDown Num(double inc, string format, double min = -100000, double max = 100000) => new()
    {
        Increment = (decimal) inc,
        FormatString = format,
        Minimum = (decimal) min,
        Maximum = (decimal) max,
        Width = 150,
        HorizontalAlignment = HorizontalAlignment.Left,
        ParsingNumberStyle = NumberStyles.Float,
        NumberFormat = NumberFormatInfo.InvariantInfo,
    };

    private static TextBlock Label(string text) => new() { Text = text, Opacity = 0.75, Margin = new Thickness(0, 8, 0, 2) };

    private static ComboBox EaseBox() => new()
    {
        ItemsSource = EditorState.Eases.Select(x => x.Name).ToList(),
        HorizontalAlignment = HorizontalAlignment.Stretch,
    };

    private Control BuildBoneTab()
    {
        _boneTitle = new TextBlock { FontSize = 16, FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap };
        _boneKeyInfo = new TextBlock { Opacity = 0.8, TextWrapping = TextWrapping.Wrap };
        _boneHint = new TextBlock { Opacity = 0.7, FontSize = 12, TextWrapping = TextWrapping.Wrap, Foreground = Brushes.LightSkyBlue };
        _boneList = new ListBox { MinHeight = 120 };
        _boneList.SelectionChanged += (_, _) =>
        {
            if (_syncing || _boneList.SelectedIndex < 0 || _boneList.SelectedIndex >= _boneTracks.Count)
                return;
            _s.Select(_boneTracks[_boneList.SelectedIndex]);
        };
        _keyXLabel = Label("X");
        _keyYLabel = Label("Y");
        _keyX = Num(1, "0.###");
        _keyY = Num(1, "0.###");
        _keyRot = Num(1, "0.#", -3600, 3600);
        _keyX.ValueChanged += (_, _) => KeyFieldChanged();
        _keyY.ValueChanged += (_, _) => KeyFieldChanged();
        _keyRot.ValueChanged += (_, _) => KeyFieldChanged();
        _keyEase = EaseBox();
        _keyEase.SelectionChanged += (_, _) =>
        {
            if (_syncing || _s.Selected is not { } tr || _keyEase.SelectedIndex < 0)
                return;
            var t = _s.FrameAtPlayhead() ?? _s.Time;
            if (!HasKey(tr, t))
                return;
            _cmd.SetKeyEase(tr, t, EditorState.Eases[_keyEase.SelectedIndex].Ease);
        };
        var key = new Button { Content = "Записать ключ (K)" };
        key.Click += (_, _) => _cmd.InsertFrame();
        var reset = new Button { Content = "Сбросить в 0" };
        reset.Click += (_, _) => _cmd.ResetSelected();
        var del = new Button { Content = "Удалить ключ" };
        del.Click += (_, _) => _cmd.DeleteSelectedKey();
        var fields = new StackPanel
        {
            Margin = new Thickness(6),
            Children =
            {
                _boneTitle,
                _boneKeyInfo,
                _keyXLabel, _keyX,
                _keyYLabel, _keyY,
                Label("Поворот, °"), _keyRot,
                Label("Плавность до этого ключа"), _keyEase,
                new WrapPanel { Margin = new Thickness(0, 8, 0, 0), Children = { key, reset, del } },
                _boneHint,
                Label("Кости и дорожки куклы"),
            },
        };
        var dock = new DockPanel();
        DockPanel.SetDock(fields, Dock.Top);
        dock.Children.Add(fields);
        dock.Children.Add(_boneList);
        return dock;
    }

    private Control BuildFrameTab()
    {
        _frameInfo = new TextBlock { TextWrapping = TextWrapping.Wrap, FontSize = 15 };
        _frameDuration = Num(0.01, "0.000", 0.01, 60);
        _ripple = new CheckBox { Content = "Сдвигать всё, что после (обе куклы, события)", IsChecked = true };
        var apply = new Button { Content = "Применить длительность" };
        apply.Click += (_, _) => _cmd.SetDuration((float) (_frameDuration.Value ?? 0.2m), _ripple.IsChecked == true);
        _frameEase = EaseBox();
        _frameEase.SelectionChanged += (_, _) =>
        {
            if (_syncing || _frameEase.SelectedIndex < 0)
                return;
            _cmd.SetFrameEase(EditorState.Eases[_frameEase.SelectedIndex].Ease);
        };
        Button B(string text, Action act)
        {
            var b = new Button { Content = text, Margin = new Thickness(0, 2), HorizontalAlignment = HorizontalAlignment.Stretch };
            b.Click += (_, _) => act();
            return b;
        }

        return new StackPanel
        {
            Margin = new Thickness(6),
            Children =
            {
                _frameInfo,
                Label("Длительность кадра (до следующего), с"),
                _frameDuration,
                _ripple,
                apply,
                Label("Плавность перехода в этот кадр (все дорожки)"),
                _frameEase,
                Label("Действия"),
                B("Записать позу в кадр (K)", _cmd.InsertFrame),
                B("Дублировать кадр (Ctrl+D)", _cmd.DuplicateFrame),
                B("Копировать позу (Ctrl+C)", _cmd.Copy),
                B("Вставить позу (Ctrl+V)", _cmd.Paste),
                B("Удалить кадр (Del)", _cmd.DeleteFrame),
                new TextBlock
                {
                    Text = "Кадр — это момент времени, где у куклы есть ключи. «Править: Обе» применяет действия к обеим куклам.",
                    TextWrapping = TextWrapping.Wrap,
                    Opacity = 0.6,
                    FontSize = 12,
                    Margin = new Thickness(0, 10, 0, 0),
                },
            },
        };
    }

    private Control BuildSceneTab()
    {
        _sceneInfo = new TextBlock { TextWrapping = TextWrapping.Wrap };
        _sceneName = new TextBox();
        _sceneName.LostFocus += (_, _) =>
        {
            if (_s.Doc is not { } doc || (_sceneName.Text ?? "") == (doc.Node.Str("editorName") ?? ""))
                return;
            var v = _sceneName.Text?.Trim() ?? "";
            doc.Edit(() =>
            {
                if (v.Length == 0)
                    doc.Node.Remove("editorName");
                else
                    doc.Node.Set("editorName", YamlText.S(v));
            });
        };
        _sceneCategory = new ComboBox { ItemsSource = Enum.GetValues<ProcAnimCategory>().Select(c => CategoryName(c.ToString())).ToList() };
        _sceneCategory.SelectionChanged += (_, _) =>
        {
            if (_syncing || _s.Doc is not { } doc || _sceneCategory.SelectedIndex < 0)
                return;
            var c = Enum.GetValues<ProcAnimCategory>()[_sceneCategory.SelectedIndex];
            if (c == doc.Proto.Category)
                return;
            doc.Edit(() => doc.Node.Set("category", YamlText.S(c.ToString())));
        };
        _sceneLength = Num(0.05, "0.00", 0.05, 120);
        _sceneLength.ValueChanged += (_, _) =>
        {
            if (_syncing || _s.Doc is not { } doc || _sceneLength.Value is not { } v || MathF.Abs((float) v - doc.Proto.Length) < 0.0005f)
                return;
            doc.Edit(() => doc.SetLength((float) v));
        };
        _problems = new SelectableTextBlock { TextWrapping = TextWrapping.Wrap, Foreground = Brushes.Orange };
        _events = new SelectableTextBlock { TextWrapping = TextWrapping.Wrap, FontSize = 12, Opacity = 0.85 };
        return new StackPanel
        {
            Margin = new Thickness(6),
            Children =
            {
                _sceneInfo,
                Label("Название в редакторе (editorName)"), _sceneName,
                Label("Категория (category)"), _sceneCategory,
                Label("Длина анимации (length), с"), _sceneLength,
                Label("Проверка"), _problems,
                Label("События (правятся во вкладке YAML)"), _events,
            },
        };
    }

    private Control BuildYamlTab()
    {
        _yaml = new TextBox
        {
            AcceptsReturn = true,
            AcceptsTab = false,
            TextWrapping = TextWrapping.NoWrap,
            FontFamily = new FontFamily("Consolas, DejaVu Sans Mono, monospace"),
            FontSize = 12,
        };
        var apply = new Button { Content = "Применить" };
        apply.Click += (_, _) => ApplyYaml();
        var reload = new Button { Content = "Обновить из сцены" };
        reload.Click += (_, _) => RefreshYaml(true);
        var bar = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 6,
            Margin = new Thickness(0, 4),
            Children =
            {
                apply, reload,
                new TextBlock { Text = "Сцена целиком; комментарии в файле сохраняются при записи", VerticalAlignment = VerticalAlignment.Center, Opacity = 0.6, FontSize = 11 },
            },
        };
        var dock = new DockPanel();
        DockPanel.SetDock(bar, Dock.Top);
        dock.Children.Add(bar);
        dock.Children.Add(_yaml);
        return dock;
    }

    private void ApplyYaml()
    {
        if (_s.Doc is not { } doc)
            return;
        try
        {
            var text = _yaml.Text ?? "";
            var ys = YamlText.Parse(text);
            var node = ys.Documents.FirstOrDefault()?.RootNode switch
            {
                YamlDotNet.RepresentationModel.YamlSequenceNode q => q.Children.FirstOrDefault() as YamlDotNet.RepresentationModel.YamlMappingNode,
                YamlDotNet.RepresentationModel.YamlMappingNode m => m,
                _ => null,
            };
            if (node == null || node.Str("type") != "procAnimScene")
                throw new InvalidDataException("ожидается одна запись «- type: procAnimScene»");
            if (node.Str("id") != doc.Id)
                throw new InvalidDataException("id менять здесь нельзя — используйте «Сохранить как копию»");
            doc.ReplaceNode(node);
            _s.SetTime(_s.Time);
            SetStatus("YAML применён" + (doc.Problems.Count > 0 ? $" · проблем: {doc.Problems.Count}" : ""));
        }
        catch (Exception e)
        {
            SetStatus("YAML не применён: " + e.Message);
            _ = Dialogs.Info(this, "Ошибка в YAML", e.Message);
        }
    }

    private void RefreshYaml(bool force)
    {
        if (_s?.Doc is not { } doc || _tabs.SelectedIndex != 3)
            return;
        if (!force && _yamlRevision == doc.Revision)
            return;
        _yaml.Text = doc.SceneText();
        _yamlRevision = doc.Revision;
    }

    private int _yamlRevision = -1;

    // ---------------- syncing panels with the state ----------------

    private void OnDocChanged()
    {
        if (_s.Doc is not { } doc)
            return;
        var dirty = doc.Dirty;
        Title = $"{doc.Id}{(dirty ? " *" : "")} — Abisfera — редактор анимаций";
        if (_shownRevision != doc.Revision)
        {
            _shownRevision = doc.Revision;
            RefreshScenePanel();
            RefreshYaml(false);
        }

        for (var i = 0; i < 2; i++)
        {
            var role = i < doc.Proto.Actors.Count ? doc.Proto.Actors[i].Role : "—";
            _puppetLabels[i].Text = $"Кукла {i + 1} ({role})";
        }

        RefreshBonePanel();
        RefreshFramePanel();
    }

    private void OnTimeChanged()
    {
        _timeText.Text = _s.Doc == null ? "" : $"{_s.Time,7:0.000} с  /  {_s.End:0.00} с";
        if (_s.Playing)
            return;
        RefreshBonePanel();
        RefreshFramePanel();
    }

    private void OnSelectionChanged()
    {
        _syncing = true;
        var idx = _s.EditActor < 0 ? 2 : Math.Min(_s.EditActor, 1);
        if (_editTarget.SelectedIndex != idx)
            _editTarget.SelectedIndex = idx;
        _syncing = false;
        RefreshBonePanel();
        RefreshFramePanel();
    }

    private bool HasKey(TrackRef tr, float t)
        => _s.Doc?.ProtoKeys(tr)?.Any(k => MathF.Abs(k.Time - t) < 0.004f) == true;

    private void RefreshBonePanel()
    {
        if (_s.Doc is not { } doc)
            return;
        _syncing = true;
        try
        {
            // the list: every editable bone of the primary actor (tracks get created on first key), root, props, IK
            var a = _s.PrimaryActor;
            var tracks = new List<TrackRef> { new(a, TrackKind.Move) };
            tracks.AddRange(Viewport.EditableBones
                .Where(b => b is not (ProcAnimBone.HeldNear or ProcAnimBone.HeldFar) || doc.Proto.Actors[a].Parts.ContainsKey(b))
                .Select(b => new TrackRef(a, TrackKind.Part, b)));
            tracks.AddRange(doc.Tracks(a, true).Where(t => t.Kind is TrackKind.Prop or TrackKind.Ik));
            var labels = tracks.Select(t => _s.TrackName(t) + (doc.ProtoKeys(t) is { Count: > 0 } k ? $"  · {k.Count}" : "")).ToList();
            if (!tracks.SequenceEqual(_boneTracks) || !labels.SequenceEqual(_boneLabels))
            {
                _boneTracks = tracks;
                _boneLabels = labels;
                _boneList.ItemsSource = labels;
            }

            _boneList.SelectedIndex = _s.Selected is { } sel ? _boneTracks.IndexOf(sel) : -1;

            if (_s.Selected is not { } tr || tr.Actor >= doc.Proto.Actors.Count)
            {
                _boneTitle.Text = "Ничего не выбрано";
                _boneKeyInfo.Text = "Кликните по части тела куклы, по суставу или выберите дорожку в списке ниже.";
                _keyX.IsEnabled = _keyY.IsEnabled = _keyRot.IsEnabled = _keyEase.IsEnabled = false;
                _boneHint.Text = "";
                return;
            }

            var t = _s.FrameAtPlayhead() ?? _s.Time;
            var has = HasKey(tr, t);
            var v = doc.KeyedValue(tr, t);
            _boneTitle.Text = $"{_s.TrackName(tr)} — кукла {tr.Actor + 1}";
            _boneKeyInfo.Text = has ? $"Ключ в {t:0.###} с" : $"В {t:0.###} с ключа нет: значения ниже — промежуточные, правка создаст ключ.";
            _keyX.IsEnabled = _keyY.IsEnabled = true;
            _keyRot.IsEnabled = tr.HasRot;
            _keyEase.IsEnabled = has;
            (_keyXLabel.Text, _keyYLabel.Text) = tr.Kind switch
            {
                TrackKind.Move => ("X, тайлы (вперёд к сопернику)", "Y, тайлы (вверх по экрану)"),
                TrackKind.Ik => ("Смещение цели X, px", "Смещение цели Y, px"),
                _ => ("Смещение X, px", "Смещение Y, px"),
            };
            _keyX.Increment = _keyY.Increment = tr.Kind == TrackKind.Move ? 0.03125m : 1m;
            _keyX.Value = (decimal) MathF.Round(v.X, 3);
            _keyY.Value = (decimal) MathF.Round(v.Y, 3);
            _keyRot.Value = (decimal) MathF.Round(v.Rot, 1);
            var ease = doc.ProtoKeys(tr)?.FirstOrDefault(k => MathF.Abs(k.Time - t) < 0.004f)?.Ease ?? ProcAnimEase.InOut;
            _keyEase.SelectedIndex = Array.FindIndex(EditorState.Eases, x => x.Ease == ease);
            var actor = doc.Proto.Actors[tr.Actor];
            _boneHint.Text = tr.Kind == TrackKind.Part && EditorState.Limit(tr.Bone, actor.Joints) is { } lim
                ? $"Ограничение сустава (в игре): {lim.Min:0}° … {lim.Max:0}° относительно родителя (с учётом facing {actor.Facing:+0;-0})."
                : "";
        }
        finally
        {
            _syncing = false;
        }
    }

    private void KeyFieldChanged()
    {
        if (_syncing || _s.Doc is not { } doc || _s.Selected is not { } tr)
            return;
        var x = (float) (_keyX.Value ?? 0);
        var y = (float) (_keyY.Value ?? 0);
        var rot = (float) (_keyRot.Value ?? 0);
        var t = EditorCommands.Snap(_s.FrameAtPlayhead() ?? _s.Time);
        doc.Edit(() => doc.WriteKey(tr, t, v => new KeyValue(x, y, tr.HasRot ? rot : v.Rot)));
    }

    private void RefreshFramePanel()
    {
        if (_s.Doc is not { } doc)
            return;
        _syncing = true;
        try
        {
            var frames = _s.Frames();
            var f = _s.FrameAtPlayhead();
            var who = _s.EditActor < 0 ? "обеих кукол" : $"куклы {_s.EditActor + 1}";
            if (f is { } t)
            {
                var n = frames.FindIndex(x => MathF.Abs(x - t) < 0.004f) + 1;
                _frameInfo.Text = $"Кадр {n} из {frames.Count} ({who}) — {t:0.###} с";
                _frameDuration.IsEnabled = _frameEase.IsEnabled = true;
                _frameDuration.Value = (decimal) MathF.Round(_cmd.DurationOf(t), 3);
                var ease = doc.FrameEase(_s.EditedActors, t) ?? ProcAnimEase.InOut;
                _frameEase.SelectedIndex = Array.FindIndex(EditorState.Eases, x => x.Ease == ease);
            }
            else
            {
                _frameInfo.Text = $"Между кадрами ({who}, всего {frames.Count}). K — записать позу здесь.";
                _frameDuration.IsEnabled = _frameEase.IsEnabled = false;
            }
        }
        finally
        {
            _syncing = false;
        }
    }

    private void RefreshScenePanel()
    {
        if (_s.Doc is not { } doc)
            return;
        _syncing = true;
        try
        {
            var p = doc.Proto;
            _sceneInfo.Text = $"{doc.Id}\n{RelPath(doc.FilePath)}{(doc.IsNew ? " (новый файл)" : "")}\n" +
                              $"Актёры: {string.Join(", ", p.Actors.Select((a, i) => $"{i + 1} — {a.Role}"))}\n" +
                              $"Полная длина с hit-stop: {ProcAnimTimeline.TotalLength(p):0.00} с" + (p.Intro > 0 ? $", вступление {p.Intro:0.##} с" : "");
            if (_sceneName.Text != (doc.Node.Str("editorName") ?? ""))
                _sceneName.Text = doc.Node.Str("editorName") ?? "";
            _sceneCategory.SelectedIndex = Array.IndexOf(Enum.GetValues<ProcAnimCategory>(), p.Category);
            _sceneLength.Value = (decimal) MathF.Round(p.Length, 3);
            _problems.Text = doc.Problems.Count == 0 ? "Проблем нет ✓" : "• " + string.Join("\n• ", doc.Problems);
            _problems.Foreground = doc.Problems.Count == 0 ? Brushes.LightGreen : Brushes.Orange;
            _events.Text = string.Join("\n", p.Events.Select(e =>
                $"{e.Time.ToString("0.###", Inv),6} с  {e.Target}{(e.Beat != null ? "  «" + e.Beat + "»" : "")}{(e.HitStop > 0 ? $"  стоп {e.HitStop:0.##}" : "")}"));
        }
        finally
        {
            _syncing = false;
        }
    }
}
