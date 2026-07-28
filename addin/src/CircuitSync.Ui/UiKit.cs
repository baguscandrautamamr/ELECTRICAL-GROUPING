using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using CircuitSync.Core;

namespace CircuitSync.Ui;

/// <summary>
/// Kumpulan pabrik kontrol WPF dengan gaya yang seragam. Dua daftar callback di sini
/// yang membuat dua bahasa dan dua tema bekerja tanpa membangun ulang window:
/// <see cref="Restyle"/> untuk warna, <see cref="Retext"/> untuk teks.
/// </summary>
public sealed class UiKit(Translator translator)
{
    private readonly List<Action<Palette>> _restyle = [];
    private readonly List<Action<Translator>> _retext = [];

    public Translator Translator { get; } = translator;

    public Palette Palette { get; private set; } = Palette.Light;

    public static readonly FontFamily Face = new("Segoe UI Variable Text, Segoe UI, San Francisco, Helvetica Neue");

    public void Restyle(Palette palette)
    {
        Palette = palette;
        foreach (var apply in _restyle)
        {
            apply(palette);
        }
    }

    public void Retext()
    {
        foreach (var apply in _retext)
        {
            apply(Translator);
        }
    }

    public T Styled<T>(T element, Action<T, Palette> apply) where T : FrameworkElement
    {
        _restyle.Add(palette => apply(element, palette));
        apply(element, Palette);
        return element;
    }

    public T Localized<T>(T element, Action<T, Translator> apply) where T : FrameworkElement
    {
        _retext.Add(t => apply(element, t));
        apply(element, Translator);
        return element;
    }

    // ------------------------------------------------------------------ text

    public TextBlock Title(string key) =>
        Localized(Styled(Base(20, FontWeights.SemiBold), (t, p) => t.Foreground = new SolidColorBrush(p.Text)),
            (t, tr) => t.Text = tr[key]);

    public TextBlock Heading(string key) =>
        Localized(Styled(Base(12, FontWeights.SemiBold), (t, p) =>
            {
                t.Foreground = new SolidColorBrush(p.TextMuted);
                t.Margin = new Thickness(0, 0, 0, 8);
            }),
            (t, tr) => t.Text = tr[key].ToUpperInvariant());

    public TextBlock Body(string key, params object?[] args) =>
        Localized(Styled(Base(13, FontWeights.Normal), (t, p) => t.Foreground = new SolidColorBrush(p.Text)),
            (t, tr) => t.Text = args.Length == 0 ? tr[key] : tr.Get(key, args));

    public TextBlock Muted(string key, params object?[] args) =>
        Localized(Styled(Base(12, FontWeights.Normal), (t, p) => t.Foreground = new SolidColorBrush(p.TextMuted)),
            (t, tr) => t.Text = args.Length == 0 ? tr[key] : tr.Get(key, args));

    /// <summary>Teks yang isinya data, bukan kunci terjemahan.</summary>
    public TextBlock Value(string initial = "")
    {
        var block = Base(13, FontWeights.Normal);
        block.Text = initial;
        return Styled(block, (t, p) => t.Foreground = new SolidColorBrush(p.Text));
    }

    private static TextBlock Base(double size, FontWeight weight) => new()
    {
        FontFamily = Face,
        FontSize = size,
        FontWeight = weight,
        TextWrapping = TextWrapping.Wrap,
    };

    // ------------------------------------------------------------------ containers

    /// <summary>Kartu bersudut tumpul, satu tingkat di atas latar.</summary>
    public Border Card(UIElement content) =>
        Styled(new Border
        {
            CornerRadius = new CornerRadius(12),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(16),
            Margin = new Thickness(0, 0, 0, 12),
            Child = content,
        }, (b, p) =>
        {
            b.Background = new SolidColorBrush(p.Surface);
            b.BorderBrush = new SolidColorBrush(p.Border);
        });

    public static StackPanel Stack(double spacing = 8, params UIElement[] children)
    {
        var panel = new StackPanel { Orientation = Orientation.Vertical };
        foreach (var child in children)
        {
            if (child is FrameworkElement element && !ReferenceEquals(child, children[0]))
            {
                element.Margin = new Thickness(element.Margin.Left, spacing, element.Margin.Right, element.Margin.Bottom);
            }

            panel.Children.Add(child);
        }

        return panel;
    }

    public static StackPanel Row(double spacing = 8, params UIElement[] children)
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal };
        foreach (var child in children)
        {
            if (child is FrameworkElement element && !ReferenceEquals(child, children[0]))
            {
                element.Margin = new Thickness(spacing, element.Margin.Top, element.Margin.Right, element.Margin.Bottom);
            }

            panel.Children.Add(child);
        }

        return panel;
    }

    // ------------------------------------------------------------------ buttons

    public Button Primary(string key, RoutedEventHandler onClick)
    {
        var button = NewButton(onClick);
        Localized(button, (b, tr) => b.Content = tr[key]);
        return Styled(button, (b, p) =>
        {
            b.Background = new SolidColorBrush(p.Accent);
            b.Foreground = new SolidColorBrush(p.AccentText);
            b.BorderBrush = new SolidColorBrush(p.Accent);
        });
    }

    public Button Secondary(string key, RoutedEventHandler onClick)
    {
        var button = NewButton(onClick);
        Localized(button, (b, tr) => b.Content = tr[key]);
        return Styled(button, (b, p) =>
        {
            b.Background = new SolidColorBrush(p.SurfaceAlt);
            b.Foreground = new SolidColorBrush(p.Text);
            b.BorderBrush = new SolidColorBrush(p.Border);
        });
    }

    /// <summary>Tombol datar yang teksnya ikut bahasa.</summary>
    public Button QuietKey(string key, RoutedEventHandler onClick)
    {
        var button = Quiet("", onClick);
        return Localized(button, (b, tr) => b.Content = tr[key]);
    }

    /// <summary>
    /// Tombol datar yang teksnya diatur pemanggil — untuk tombol tema dan bahasa,
    /// yang labelnya menyebut tujuan, bukan keadaan sekarang.
    /// </summary>
    public Button Quiet(string initialText, RoutedEventHandler onClick)
    {
        var button = NewButton(onClick);
        button.Content = initialText;
        button.Padding = new Thickness(10, 4, 10, 4);
        button.FontSize = 12;
        return Styled(button, (b, p) =>
        {
            b.Background = Brushes.Transparent;
            b.Foreground = new SolidColorBrush(p.TextMuted);
            b.BorderBrush = new SolidColorBrush(p.Border);
        });
    }

    private Button NewButton(RoutedEventHandler onClick)
    {
        var button = new Button
        {
            FontFamily = Face,
            FontSize = 13,
            FontWeight = FontWeights.SemiBold,
            Padding = new Thickness(14, 8, 14, 8),
            BorderThickness = new Thickness(1),
            Cursor = Cursors.Hand,
            HorizontalAlignment = HorizontalAlignment.Left,
            Template = RoundedButtonTemplate(),
        };
        button.Click += onClick;
        return button;
    }

    /// <summary>
    /// Template minimal supaya sudut tombol benar-benar tumpul. Template bawaan WPF
    /// menggambar chrome Windows klasik yang tidak bisa dibuat mengikuti palette.
    /// </summary>
    private static ControlTemplate RoundedButtonTemplate()
    {
        var border = new FrameworkElementFactory(typeof(Border), "root");
        border.SetValue(Border.CornerRadiusProperty, new CornerRadius(8));
        border.SetBinding(Border.BackgroundProperty, new System.Windows.Data.Binding("Background") { RelativeSource = System.Windows.Data.RelativeSource.TemplatedParent });
        border.SetBinding(Border.BorderBrushProperty, new System.Windows.Data.Binding("BorderBrush") { RelativeSource = System.Windows.Data.RelativeSource.TemplatedParent });
        border.SetBinding(Border.BorderThicknessProperty, new System.Windows.Data.Binding("BorderThickness") { RelativeSource = System.Windows.Data.RelativeSource.TemplatedParent });
        border.SetBinding(Border.PaddingProperty, new System.Windows.Data.Binding("Padding") { RelativeSource = System.Windows.Data.RelativeSource.TemplatedParent });

        var presenter = new FrameworkElementFactory(typeof(ContentPresenter));
        presenter.SetValue(ContentPresenter.HorizontalAlignmentProperty, HorizontalAlignment.Center);
        presenter.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);
        border.AppendChild(presenter);

        var template = new ControlTemplate(typeof(Button)) { VisualTree = border };

        var hover = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
        hover.Setters.Add(new Setter(UIElement.OpacityProperty, 0.85, "root"));
        template.Triggers.Add(hover);

        var disabled = new Trigger { Property = UIElement.IsEnabledProperty, Value = false };
        disabled.Setters.Add(new Setter(UIElement.OpacityProperty, 0.4, "root"));
        template.Triggers.Add(disabled);

        return template;
    }

    // ------------------------------------------------------------------ inputs

    public TextBox Field()
    {
        var box = new TextBox
        {
            FontFamily = Face,
            FontSize = 13,
            Padding = new Thickness(10, 8, 10, 8),
            BorderThickness = new Thickness(1),
        };

        return Styled(box, (b, p) =>
        {
            b.Background = new SolidColorBrush(p.SurfaceAlt);
            b.Foreground = new SolidColorBrush(p.Text);
            b.BorderBrush = new SolidColorBrush(p.Border);
            b.CaretBrush = new SolidColorBrush(p.Text);
        });
    }

    public PasswordBox Secret()
    {
        var box = new PasswordBox
        {
            FontFamily = Face,
            FontSize = 13,
            Padding = new Thickness(10, 8, 10, 8),
            BorderThickness = new Thickness(1),
        };

        return Styled(box, (b, p) =>
        {
            b.Background = new SolidColorBrush(p.SurfaceAlt);
            b.Foreground = new SolidColorBrush(p.Text);
            b.BorderBrush = new SolidColorBrush(p.Border);
            b.CaretBrush = new SolidColorBrush(p.Text);
        });
    }

    /// <summary>
    /// Label kecil di atas kontrol. Lebih jelas daripada placeholder di dalam kotak:
    /// labelnya tetap terbaca setelah user mulai mengisi.
    /// </summary>
    public StackPanel Labeled(string key, FrameworkElement control)
    {
        var label = Base(11, FontWeights.SemiBold);
        Localized(label, (l, tr) => l.Text = tr[key]);
        Styled(label, (l, p) => l.Foreground = new SolidColorBrush(p.TextMuted));

        control.Margin = new Thickness(0, 4, 0, 0);

        var panel = new StackPanel { Orientation = Orientation.Vertical };
        panel.Children.Add(label);
        panel.Children.Add(control);
        return panel;
    }

    public ComboBox Picker()
    {
        var combo = new ComboBox
        {
            FontFamily = Face,
            FontSize = 13,
            Padding = new Thickness(8, 6, 8, 6),
        };

        return Styled(combo, (c, p) =>
        {
            c.Background = new SolidColorBrush(p.SurfaceAlt);
            c.Foreground = new SolidColorBrush(p.Text);
            c.BorderBrush = new SolidColorBrush(p.Border);
        });
    }

    public CheckBox Toggle(string key, params object?[] args)
    {
        var box = new CheckBox
        {
            FontFamily = Face,
            FontSize = 13,
            VerticalContentAlignment = VerticalAlignment.Center,
        };
        Localized(box, (b, tr) => b.Content = args.Length == 0 ? tr[key] : tr.Get(key, args));
        return Styled(box, (b, p) => b.Foreground = new SolidColorBrush(p.Text));
    }

}
