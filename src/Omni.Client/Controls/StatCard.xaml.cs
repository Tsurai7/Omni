namespace Omni.Client.Controls;

public partial class StatCard : ContentView
{
    public static readonly BindableProperty ValueProperty =
        BindableProperty.Create(nameof(Value), typeof(string), typeof(StatCard), string.Empty,
            propertyChanged: (b, _, n) => ((StatCard)b).ValueLabel.Text = (string)n);

    public static readonly BindableProperty CaptionProperty =
        BindableProperty.Create(nameof(Caption), typeof(string), typeof(StatCard), string.Empty,
            propertyChanged: (b, _, n) => ((StatCard)b).CaptionLabel.Text = (string)n);

    public static readonly BindableProperty AccentColorProperty =
        BindableProperty.Create(nameof(AccentColor), typeof(Color), typeof(StatCard), Colors.White,
            propertyChanged: (b, _, n) => ((StatCard)b).ValueLabel.TextColor = (Color)n);

    public string Value
    {
        get => (string)GetValue(ValueProperty);
        set => SetValue(ValueProperty, value);
    }

    public string Caption
    {
        get => (string)GetValue(CaptionProperty);
        set => SetValue(CaptionProperty, value);
    }

    public Color AccentColor
    {
        get => (Color)GetValue(AccentColorProperty);
        set => SetValue(AccentColorProperty, value);
    }

    public StatCard()
    {
        InitializeComponent();
    }
}
