using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using NetBanner.Core;
using NetBanner.UI;
using NetBanner.Windows;

namespace NetBanner.Config;

/// <summary>
/// Edits the local config file with a live preview. The preview shows what users actually see:
/// Group Policy (new and legacy) merged above the values being edited.
/// </summary>
internal sealed class MainWindow : Window
{
    private static readonly IBrush NoteBrush = new SolidColorBrush(Color.Parse("#9A5B00"));
    private static readonly IBrush WarnBrush = new SolidColorBrush(Color.Parse("#B00020"));

    private const int ModeNotSet = 0, ModeClassification = 1, ModeCustom = 2;

    private readonly ConfigOptions _options;
    private readonly bool _canSave;
    private readonly PolicyChangeMonitor? _policyMonitor;

    // Marking
    private readonly ComboBox _mode = Combo("Not set", "Classification", "Custom banner");
    private readonly ComboBox _classification = new() { ItemsSource = MarkingCatalog.All.Select(m => m.Text).ToList(), MinWidth = 260 };
    private readonly TextBox _caveats = new() { PlaceholderText = "Optional, e.g. NOFORN (appended after //)" };
    private readonly TextBox _customText = new() { PlaceholderText = "Banner text (tokens allowed)" };
    private readonly TextBox _customBg = new() { PlaceholderText = "#FFFFFF" };
    private readonly TextBox _customFg = new() { PlaceholderText = "#000000" };
    private readonly StackPanel _classificationFields = new() { Spacing = 8 };
    private readonly StackPanel _customFields = new() { Spacing = 8 };

    // Side items
    // NetBanner server
    private readonly TextBox _serverUrl = new() { PlaceholderText = "e.g. http://netbanner-server:8080 (empty: no server)", MinWidth = 320 };
    private readonly TextBlock _serverInfo = new() { FontSize = 12, Opacity = 0.75, TextWrapping = TextWrapping.Wrap };
    private readonly Button _forgetKey = new() { Content = "Forget server key" };
    private string? _savedServerUrl;

    private readonly SideItemsEditor _left = new();
    private readonly SideItemsEditor _right = new();

    // Appearance
    private readonly NumericUpDown _height = Number(SettingsValidator.MinHeight, SettingsValidator.MaxHeight, BannerSettings.Default.Height);
    private readonly NumericUpDown _fontSize = Number((int)SettingsValidator.MinFontSize, (int)SettingsValidator.MaxFontSize, BannerSettings.Default.FontSize);
    private readonly ComboBox _fullScreen = Combo("Default (stay on top)", "Stay on top (always visible)", "Yield to full-screen applications");

    // Policy notes
    private readonly TextBlock _markingNote = Note();
    private readonly TextBlock _leftNote = Note();
    private readonly TextBlock _rightNote = Note();
    private readonly TextBlock _appearanceNote = Note();

    // Preview and status
    private readonly BannerStrip _preview = new() { VerticalAlignment = VerticalAlignment.Top };
    private readonly TextBlock _previewCaption = new() { FontSize = 12, Opacity = 0.75 };
    private readonly TextBlock _warnings = new() { Foreground = WarnBrush, TextWrapping = TextWrapping.Wrap, IsVisible = false };
    private readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center };
    private readonly Button _save = new() { Content = "Save", MinWidth = 100, HorizontalContentAlignment = HorizontalAlignment.Center, Classes = { "accent" } };

    private PartialSettings? _policy;
    private PartialSettings? _legacy;
    private bool _loading;
    private bool _dirty;
    private string _savedJson = string.Empty;
    private bool _closeConfirmed;

    public MainWindow(ConfigOptions options)
    {
        _options = options;
        _canSave = ConfigStore.IsElevated() || !options.IsDefaultPath;

        if (typeof(MainWindow).Assembly.GetManifestResourceStream("NetBanner.ico") is { } icon)
        {
            Icon = new WindowIcon(icon);
        }

        Width = 1100;
        Height = 860;
        MinWidth = 820;
        MinHeight = 600;
        WindowStartupLocation = WindowStartupLocation.Manual;
        Opened += (_, _) => CenterOnPrimaryScreen();

        Content = BuildLayout();
        WireEvents();

        if (!options.NoPolicy)
        {
            _policyMonitor = new PolicyChangeMonitor();
            _policyMonitor.Changed += (_, _) => Dispatcher.UIThread.Post(() =>
            {
                LoadPolicies();
                UpdatePreview();
                SetStatus("Group Policy changed; preview updated.");
            });
            _policyMonitor.Start();
        }

        LoadPolicies();
        LoadFromDisk();
    }

    // ---------------------------------------------------------------- layout

    private Control BuildLayout()
    {
        var importButton = new Button { Content = "Import…" };
        importButton.Click += async (_, _) => await ImportAsync();
        var exportButton = new Button { Content = "Export…" };
        exportButton.Click += async (_, _) => await ExportAsync();
        var reloadButton = new Button { Content = "Reload" };
        reloadButton.Click += async (_, _) =>
        {
            if (!_dirty || await ConfirmDiscard())
            {
                LoadFromDisk();
            }
        };
        _save.IsEnabled = _canSave;
        _save.Click += (_, _) => Save();

        var previewScreen = new Border
        {
            Background = new SolidColorBrush(Color.Parse("#1F1F1F")),
            Height = 70,
            CornerRadius = new CornerRadius(4),
            ClipToBounds = true,
            Child = _preview,
        };

        var header = new StackPanel
        {
            Spacing = 6,
            Margin = new Thickness(20, 16, 20, 10),
            Children =
            {
                new TextBlock { Text = "Preview", FontWeight = FontWeight.SemiBold },
                previewScreen,
                _previewCaption,
            },
        };

        _classificationFields.Children.Add(Row("Classification", _classification));
        _classificationFields.Children.Add(Row("Caveats", _caveats));
        _customFields.Children.Add(Row("Text", _customText));
        _customFields.Children.Add(Row("Background", _customBg));
        _customFields.Children.Add(Row("Text color", _customFg));

        var form = new StackPanel
        {
            Spacing = 14,
            Margin = new Thickness(20, 4, 20, 20),
            Children =
            {
                Section("Marking", _markingNote,
                    Row("Center shows", _mode), _classificationFields, _customFields),
                Section("Left side items", _leftNote, _left),
                Section("Right side items", _rightNote, _right),
                Section("Appearance", _appearanceNote,
                    Row("Height (px)", _height),
                    Row("Font size", _fontSize),
                    Row("Full screen", _fullScreen),
                    new TextBlock { Text = "Leave height or font size empty to use the default.", Opacity = 0.6, FontSize = 12 }),
                Section("NetBanner server", Note(),
                    Row("Server", _serverUrl), _serverInfo, _forgetKey),
                _warnings,
            },
        };

        var footer = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto"),
            Margin = new Thickness(20, 10),
        };
        footer.Children.Add(_status);
        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            Children = { importButton, exportButton, reloadButton, _save },
        };
        Grid.SetColumn(buttons, 1);
        footer.Children.Add(buttons);

        var root = new DockPanel();
        DockPanel.SetDock(header, Dock.Top);
        root.Children.Add(header);
        var footerBorder = new Border
        {
            BorderBrush = new SolidColorBrush(Color.Parse("#DDDDDD")),
            BorderThickness = new Thickness(0, 1, 0, 0),
            Child = footer,
        };
        DockPanel.SetDock(footerBorder, Dock.Bottom);
        root.Children.Add(footerBorder);
        root.Children.Add(new ScrollViewer { Content = form });
        return root;
    }

    /// <summary>
    /// Avalonia's CenterScreen can place the window partly off-screen on multi-monitor layouts with
    /// negative coordinates, so center on the primary monitor's work area manually.
    /// </summary>
    private void CenterOnPrimaryScreen()
    {
        if (Screens.Primary is not { } screen)
        {
            return;
        }

        var area = screen.WorkingArea;
        var scale = screen.Scaling;
        int w = (int)Math.Min(Bounds.Width * scale, area.Width), h = (int)Math.Min(Bounds.Height * scale, area.Height);
        Position = new PixelPoint(area.X + (area.Width - w) / 2, area.Y + (area.Height - h) / 2);
    }

    private static Control Section(string title, TextBlock note, params Control[] content)
    {
        var panel = new StackPanel { Spacing = 8 };
        panel.Children.Add(new TextBlock { Text = title, FontSize = 16, FontWeight = FontWeight.SemiBold });
        panel.Children.Add(note);
        foreach (var c in content)
        {
            panel.Children.Add(c);
        }

        return new Border
        {
            BorderBrush = new SolidColorBrush(Color.Parse("#DDDDDD")),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(14),
            Child = panel,
        };
    }

    private static Control Row(string label, Control control)
    {
        var g = new Grid { ColumnDefinitions = new ColumnDefinitions("140,*"), ColumnSpacing = 10 };
        g.Children.Add(new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center });
        if (control is TextBox or NumericUpDown or ComboBox)
        {
            control.HorizontalAlignment = control is TextBox ? HorizontalAlignment.Stretch : HorizontalAlignment.Left;
        }

        Grid.SetColumn(control, 1);
        g.Children.Add(control);
        return g;
    }

    private static ComboBox Combo(params string[] items) => new() { ItemsSource = items, SelectedIndex = 0, MinWidth = 260 };

    private static NumericUpDown Number(int min, int max, double placeholder) => new()
    {
        Minimum = min,
        Maximum = max,
        Increment = 1,
        FormatString = "0",
        Value = null,
        MinWidth = 160,
        PlaceholderText = $"Default ({placeholder:0})",
    };

    private static TextBlock Note() => new() { Foreground = NoteBrush, TextWrapping = TextWrapping.Wrap, IsVisible = false };

    private void WireEvents()
    {
        _mode.SelectionChanged += (_, _) =>
        {
            UpdateModeVisibility();
            OnEdited();
        };
        _classification.SelectionChanged += (_, _) => OnEdited();
        foreach (var tb in new[] { _caveats, _customText, _customBg, _customFg, _serverUrl })
        {
            tb.TextChanged += (_, _) => OnEdited();
        }

        _forgetKey.Click += async (_, _) => await ForgetServerKeyAsync();

        _left.Changed += OnEdited;
        _right.Changed += OnEdited;
        _height.ValueChanged += (_, _) => OnEdited();
        _fontSize.ValueChanged += (_, _) => OnEdited();
        _fullScreen.SelectionChanged += (_, _) => OnEdited();
    }

    private void UpdateModeVisibility()
    {
        _classificationFields.IsVisible = _mode.SelectedIndex == ModeClassification;
        _customFields.IsVisible = _mode.SelectedIndex == ModeCustom;
    }

    // ---------------------------------------------------------------- model <-> form

    private PartialSettings BuildDraft()
    {
        var mode = _mode.SelectedIndex;
        return new PartialSettings(ConfigFile.SourceName)
        {
            Classification = mode == ModeClassification && _classification.SelectedIndex >= 0
                ? MarkingCatalog.All[_classification.SelectedIndex].Id
                : null,
            Caveats = mode == ModeClassification ? Blank(_caveats.Text) : null,
            Custom = mode == ModeCustom
                ? new CustomBanner(_customText.Text?.Trim() ?? string.Empty, Blank(_customBg.Text) ?? "#FFFFFF", Blank(_customFg.Text) ?? "#000000")
                : null,
            LeftItems = _left.Get(),
            RightItems = _right.Get(),
            Height = _height.Value is { } h ? (int)h : null,
            FontSize = _fontSize.Value is { } f ? (double)f : null,
            FullScreenBehavior = _fullScreen.SelectedIndex switch
            {
                1 => FullScreenBehavior.StayOnTop,
                2 => FullScreenBehavior.Yield,
                _ => null,
            },
            ServerUrl = Blank(_serverUrl.Text)?.TrimEnd('/'),
        };
    }

    private void Populate(PartialSettings? s)
    {
        _loading = true;
        try
        {
            s ??= new PartialSettings(ConfigFile.SourceName);

            if (s.Custom is { } c)
            {
                _mode.SelectedIndex = ModeCustom;
                _customText.Text = c.Text;
                _customBg.Text = c.Background;
                _customFg.Text = c.Foreground;
            }
            else if (s.Classification is not null && MarkingCatalog.TryGet(s.Classification, out var m)
                     && m.Id != MarkingCatalog.NotConfiguredId)
            {
                _mode.SelectedIndex = ModeClassification;
                _classification.SelectedIndex = MarkingCatalog.All.ToList().FindIndex(x => x.Id == m.Id);
                _caveats.Text = s.Caveats;
            }
            else
            {
                _mode.SelectedIndex = ModeNotSet;
                _classification.SelectedIndex = -1;
                _caveats.Text = null;
            }

            if (s.Custom is null)
            {
                _customText.Text = _customBg.Text = _customFg.Text = null;
            }

            _serverUrl.Text = s.ServerUrl;
            _left.Set(s.LeftItems);
            _right.Set(s.RightItems);
            _height.Value = s.Height;
            _fontSize.Value = s.FontSize is { } f ? (decimal)f : null;
            _fullScreen.SelectedIndex = s.FullScreenBehavior switch
            {
                FullScreenBehavior.StayOnTop => 1,
                FullScreenBehavior.Yield => 2,
                _ => 0,
            };
            UpdateModeVisibility();
        }
        finally
        {
            _loading = false;
        }

        UpdatePreview();
    }

    private void OnEdited()
    {
        if (_loading)
        {
            return;
        }

        // Compare with the last saved/loaded state rather than counting edits: some controls
        // raise change events late, and editing a value back to the original isn't "dirty".
        SetDirty(ConfigFile.Serialize(BuildDraft()) != _savedJson);
        UpdatePreview();
    }

    // ---------------------------------------------------------------- preview and policy

    private void LoadPolicies()
    {
        if (_options.NoPolicy)
        {
            return;
        }

        _policy = PolicySource.Read();
        _legacy = LegacyPolicySource.Read();
    }

    private void UpdatePreview()
    {
        var ignored = new List<string>();
        var warnings = new List<string>();
        var draft = SettingsValidator.Normalize(BuildDraft(), warnings);
        // Same order as the banner: Group Policy > NetBanner server (if this config points at one) > this file.
        var server = draft.ServerUrl is null ? null : ServerSync.LoadCached(ConfigStore.StateDir, ignored);
        var sources = new[] { _policy, _legacy, server }
            .OfType<PartialSettings>()
            .Select(p => SettingsValidator.Normalize(p, ignored))
            .Append(draft)
            .ToList();
        UpdateServerInfo(draft.ServerUrl, server is not null);

        var merged = SettingsMerger.Merge(sources);
        _preview.Height = merged.Settings.Height;
        _preview.Render(merged.Settings, Tokens.FromSystem);

        var origin = merged.Provenance;
        SetNote(_markingNote, origin[nameof(BannerSettings.Classification)], "The center marking");
        SetNote(_leftNote, origin[nameof(BannerSettings.LeftItems)], "The left side");
        SetNote(_rightNote, origin[nameof(BannerSettings.RightItems)], "The right side");
        var appearanceSource = new[] { nameof(BannerSettings.Height), nameof(BannerSettings.FontSize), nameof(BannerSettings.FullScreenBehavior) }
            .Select(f => origin[f]).FirstOrDefault(IsPolicy);
        SetNote(_appearanceNote, appearanceSource ?? SettingsMerger.DefaultSource, "Some appearance settings");

        _previewCaption.Text = $"Marking from: {Describe(origin[nameof(BannerSettings.Classification)])}  ·  " +
                               $"Left: {Describe(origin[nameof(BannerSettings.LeftItems)])}  ·  " +
                               $"Right: {Describe(origin[nameof(BannerSettings.RightItems)])}";

        _warnings.Text = string.Join(Environment.NewLine, warnings.Select(w => "⚠ " + w.Replace(ConfigFile.SourceName + ": ", string.Empty)));
        _warnings.IsVisible = warnings.Count > 0;
    }

    /// <summary>Sources that outrank this file: Group Policy and the NetBanner server.</summary>
    private static bool IsPolicy(string source) =>
        source is PolicyMapper.SourceName or LegacyPolicyMapper.SourceName or ServerConfig.SourceName;

    private static string Describe(string source) => source switch
    {
        PolicyMapper.SourceName => "Group Policy (NetBanner 3)",
        LegacyPolicyMapper.SourceName => "legacy NetBanner Group Policy",
        ServerConfig.SourceName => "the NetBanner server",
        ConfigFile.SourceName => "this config",
        _ => "defaults",
    };

    private static void SetNote(TextBlock note, string source, string what)
    {
        note.IsVisible = IsPolicy(source);
        note.Text = $"{what} is set by {Describe(source)}, which overrides this file. Users see that value. " +
                    "Values here only apply if it stops setting them, so they still work as a safe fallback.";
    }

    private void UpdateServerInfo(string? url, bool haveConfig)
    {
        var pinned = ServerSync.PinnedFingerprint(ConfigStore.StateDir);
        _forgetKey.IsVisible = pinned is not null && _canSave;
        _serverInfo.Text = url is null
            ? "Not using a NetBanner server. Settings come from Group Policy and this file."
            : $"Pinned server key: {pinned ?? "none yet (pinned on first contact)"}  ·  " +
              (haveConfig ? "Server config received." : "No server config received yet.") +
              "  The machine checks the server every 60 seconds; the server's settings override this file.";
    }

    private async Task ForgetServerKeyAsync()
    {
        if (!await ConfirmDialog.Show(this, "Forget server key",
                "Only do this after the server's key was deliberately changed. The machine will trust the key the " +
                "server presents on its next sync, and drop the cached server config until then. Continue?",
                "Forget key", "Cancel"))
        {
            return;
        }

        try
        {
            ServerSync.ForgetServerKey(ConfigStore.StateDir);
            TaskRegistration.StartSync();
            SetStatus("Server key forgotten. The sync task restarted and will pin the server's current key.");
        }
        catch (Exception ex)
        {
            SetStatus($"Could not forget the server key: {ex.Message}");
        }

        await Task.Delay(3000);
        UpdatePreview();
    }

    // ---------------------------------------------------------------- file operations

    private void LoadFromDisk()
    {
        var warnings = new List<string>();
        var settings = ConfigFile.Load(_options.ConfigPath, warnings);
        Populate(settings);
        MarkClean();

        var exists = File.Exists(_options.ConfigPath);
        SetStatus(warnings.Count > 0
            ? $"Problems reading {_options.ConfigPath}: {string.Join(" ", warnings)}"
            : exists ? $"Loaded {_options.ConfigPath}" : $"{_options.ConfigPath} doesn't exist yet. Saving creates it.");
    }

    private void Save()
    {
        if (!_canSave)
        {
            return;
        }

        try
        {
            var draft = BuildDraft();
            var json = ConfigFile.Serialize(draft);
            ConfigStore.Save(_options.ConfigPath, json, secureFolder: _options.IsDefaultPath && ConfigStore.IsElevated());
            var serverChanged = _options.IsDefaultPath && draft.ServerUrl != _savedServerUrl;
            MarkClean();
            SetStatus($"Saved {_options.ConfigPath} at {DateTime.Now:HH:mm:ss}. Running banners update within a second.");

            // A new server address takes effect now rather than at the next 60-second check.
            if (serverChanged)
            {
                TaskRegistration.StartSync();
                SetStatus($"Saved {_options.ConfigPath} at {DateTime.Now:HH:mm:ss}. Server sync restarted; " +
                          "the server's settings arrive within a few seconds.");
                _ = RefreshAfterSyncAsync();
            }
        }
        catch (Exception ex)
        {
            SetStatus($"Save failed: {ex.Message}");
        }
    }

    private async Task ImportAsync()
    {
        if (_dirty && !await ConfirmDiscard())
        {
            return;
        }

        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Import NetBanner config",
            AllowMultiple = false,
            FileTypeFilter = [new FilePickerFileType("NetBanner config (*.json)") { Patterns = ["*.json"] }],
        });
        if (files.Count == 0)
        {
            return;
        }

        await using var stream = await files[0].OpenReadAsync();
        using var reader = new StreamReader(stream);
        var warnings = new List<string>();
        var settings = ConfigFile.Parse(await reader.ReadToEndAsync(), warnings);
        Populate(settings);
        SetDirty(true);
        SetStatus($"Imported {files[0].Name}. Review, then Save to apply." + (warnings.Count > 0 ? " " + string.Join(" ", warnings) : string.Empty));
    }

    private async Task ExportAsync()
    {
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Export NetBanner config",
            SuggestedFileName = "config.json",
            DefaultExtension = "json",
            FileTypeChoices = [new FilePickerFileType("NetBanner config (*.json)") { Patterns = ["*.json"] }],
        });
        if (file is null)
        {
            return;
        }

        await using var stream = await file.OpenWriteAsync();
        await using var writer = new StreamWriter(stream);
        await writer.WriteAsync(ConfigFile.Serialize(BuildDraft()));
        SetStatus($"Exported to {file.Name}.");
    }

    // ---------------------------------------------------------------- state

    private void SetDirty(bool dirty)
    {
        _dirty = dirty;
        Title = "NetBanner Configuration" + (dirty ? " *" : string.Empty) + (_canSave ? string.Empty : " (read-only)");
    }

    private void MarkClean()
    {
        var draft = BuildDraft();
        _savedJson = ConfigFile.Serialize(draft);
        _savedServerUrl = draft.ServerUrl;
        SetDirty(false);
    }

    private async Task RefreshAfterSyncAsync()
    {
        await Task.Delay(4000);
        UpdatePreview();
    }

    private void SetStatus(string text) => _status.Text = text;

    private Task<bool> ConfirmDiscard() =>
        ConfirmDialog.Show(this, "Unsaved changes", "You have unsaved changes. Discard them?", "Discard", "Cancel");

    protected override async void OnClosing(WindowClosingEventArgs e)
    {
        base.OnClosing(e);
        if (!_dirty || _closeConfirmed || !_canSave)
        {
            return;
        }

        e.Cancel = true;
        if (await ConfirmDiscard())
        {
            _closeConfirmed = true;
            Close();
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        _policyMonitor?.Dispose();
        base.OnClosed(e);
    }

    private static string? Blank(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
}
