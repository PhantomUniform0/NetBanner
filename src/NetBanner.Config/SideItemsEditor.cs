using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using NetBanner.Core;

namespace NetBanner.Config;

/// <summary>Edits an ordered list of side items (one row per item).</summary>
internal sealed class SideItemsEditor : StackPanel
{
    private readonly StackPanel _rows = new() { Spacing = 6 };
    private readonly TextBlock _empty = new()
    {
        Text = "No items. Lower sources or defaults apply to this side.",
        Opacity = 0.6,
        FontStyle = Avalonia.Media.FontStyle.Italic,
    };

    private readonly List<ItemRow> _items = [];
    private bool _loading;

    public event Action? Changed;

    public SideItemsEditor()
    {
        Spacing = 6;
        Children.Add(_empty);
        Children.Add(_rows);

        var add = new Button { Content = "+ Add item", HorizontalAlignment = HorizontalAlignment.Left };
        add.Click += (_, _) =>
        {
            AddRow(new SideItem(SideItemType.Text, string.Empty));
            Raise();
        };
        Children.Add(add);
    }

    public void Set(IReadOnlyList<SideItem>? items)
    {
        _loading = true;
        _rows.Children.Clear();
        _items.Clear();
        foreach (var item in items ?? [])
        {
            AddRow(item);
        }

        _loading = false;
        _empty.IsVisible = _items.Count == 0;
    }

    /// <summary>Null when there are no rows, so the side falls through to lower sources.</summary>
    public IReadOnlyList<SideItem>? Get() => _items.Count == 0 ? null : _items.Select(r => r.ToItem()).ToList();

    private void AddRow(SideItem item)
    {
        var row = new ItemRow(item);
        row.Changed += Raise;
        row.Remove += () =>
        {
            _items.Remove(row);
            _rows.Children.Remove(row);
            Raise();
        };
        row.Move += delta =>
        {
            int i = _items.IndexOf(row), j = i + delta;
            if (j < 0 || j >= _items.Count)
            {
                return;
            }

            _items.RemoveAt(i);
            _items.Insert(j, row);
            _rows.Children.RemoveAt(i);
            _rows.Children.Insert(j, row);
            Raise();
        };
        _items.Add(row);
        _rows.Children.Add(row);
    }

    private void Raise()
    {
        _empty.IsVisible = _items.Count == 0;
        if (!_loading)
        {
            Changed?.Invoke();
        }
    }

    /// <summary>One item: type, value, optional colors, reorder/remove buttons.</summary>
    private sealed class ItemRow : Grid
    {
        private static readonly (SideItemType Type, string Label)[] Types =
        [
            (SideItemType.Text, "Text"),
            (SideItemType.Cpcon, "CPCON"),
            (SideItemType.Fpcon, "FPCON"),
            (SideItemType.Infocon, "INFOCON (legacy)"),
        ];

        private static readonly string[] Levels = ["1", "2", "3", "4", "5"];

        private readonly ComboBox _type = new() { ItemsSource = Types.Select(t => t.Label).ToList(), HorizontalAlignment = HorizontalAlignment.Stretch };
        private readonly TextBox _text = new() { PlaceholderText = "Text (tokens: {hostname} {username} {domain} {ip})" };
        private readonly ComboBox _level = new() { HorizontalAlignment = HorizontalAlignment.Stretch };
        private readonly TextBox _bg = new() { PlaceholderText = "Background" };
        private readonly TextBox _fg = new() { PlaceholderText = "Text color" };
        private bool _loading = true;

        public event Action? Changed;
        public event Action? Remove;
        public event Action<int>? Move;

        public ItemRow(SideItem item)
        {
            ColumnDefinitions = new ColumnDefinitions("140,*,130,130,Auto,Auto,Auto");
            ColumnSpacing = 6;
            ToolTip.SetTip(_bg, "Optional background color, #RRGGBB. Empty = banner color.");
            ToolTip.SetTip(_fg, "Optional text color, #RRGGBB. Empty = banner text color.");

            Add(_type, 0);
            Add(_text, 1);
            Add(_level, 1);
            Add(_bg, 2);
            Add(_fg, 3);
            Add(Button("↑", "Move up", () => Move?.Invoke(-1)), 4);
            Add(Button("↓", "Move down", () => Move?.Invoke(1)), 5);
            Add(Button("✕", "Remove", () => Remove?.Invoke()), 6);

            _type.SelectedIndex = Math.Max(0, Array.FindIndex(Types, t => t.Type == item.Type));
            ShowValueEditor(item.Value);
            _bg.Text = item.Background;
            _fg.Text = item.Foreground;

            _type.SelectionChanged += (_, _) =>
            {
                if (_loading)
                {
                    return;
                }

                ShowValueEditor(CurrentValue);
                Changed?.Invoke();
            };
            _text.TextChanged += (_, _) => RaiseChanged();
            _level.SelectionChanged += (_, _) => RaiseChanged();
            _bg.TextChanged += (_, _) => RaiseChanged();
            _fg.TextChanged += (_, _) => RaiseChanged();
            _loading = false;
        }

        private SideItemType Type => Types[Math.Max(0, _type.SelectedIndex)].Type;

        private string CurrentValue => Type == SideItemType.Text ? _text.Text ?? string.Empty : _level.SelectedItem as string ?? string.Empty;

        public SideItem ToItem() => new(Type, CurrentValue.Trim(), Blank(_bg.Text), Blank(_fg.Text));

        private void ShowValueEditor(string value)
        {
            bool wasLoading = _loading;
            _loading = true;
            bool isText = Type == SideItemType.Text;
            _text.IsVisible = isText;
            _level.IsVisible = !isText;
            if (isText)
            {
                _text.Text = value;
            }
            else
            {
                var options = Type == SideItemType.Fpcon ? SettingsValidator.FpconLevels.ToArray() : Levels;
                _level.ItemsSource = options;
                var idx = Array.FindIndex(options, o => string.Equals(o, value?.Trim(), StringComparison.OrdinalIgnoreCase));
                _level.SelectedIndex = idx >= 0 ? idx : (Type == SideItemType.Fpcon ? 0 : 4);
            }

            _loading = wasLoading;
        }

        private void RaiseChanged()
        {
            if (!_loading)
            {
                Changed?.Invoke();
            }
        }

        private void Add(Control c, int column)
        {
            SetColumn(c, column);
            Children.Add(c);
        }

        private static Button Button(string text, string tip, Action click)
        {
            var b = new Button { Content = text, MinWidth = 32, HorizontalContentAlignment = HorizontalAlignment.Center };
            ToolTip.SetTip(b, tip);
            b.Click += (_, _) => click();
            return b;
        }

        private static string? Blank(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
    }
}
