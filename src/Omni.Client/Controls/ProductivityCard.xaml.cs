using System.Windows.Input;

namespace Omni.Client.Controls;

/// <summary>
/// Reusable card container with productivity design tokens (background, stroke, radius, shadow).
/// Add child content in XAML or set Content in code. Optional Command for tap-to-action cards.
/// </summary>
public partial class ProductivityCard : ContentView
{
    public static readonly BindableProperty CommandProperty = BindableProperty.Create(
        nameof(Command), typeof(ICommand), typeof(ProductivityCard));

    public static readonly BindableProperty CommandParameterProperty = BindableProperty.Create(
        nameof(CommandParameter), typeof(object), typeof(ProductivityCard));

    public ICommand? Command
    {
        get => (ICommand?)GetValue(CommandProperty);
        set => SetValue(CommandProperty, value);
    }

    public object? CommandParameter
    {
        get => GetValue(CommandParameterProperty);
        set => SetValue(CommandParameterProperty, value);
    }

    public ProductivityCard()
    {
        InitializeComponent();
        var tap = new TapGestureRecognizer();
        tap.Tapped += (s, e) =>
        {
            if (Command?.CanExecute(CommandParameter) == true)
                Command.Execute(CommandParameter);
        };
        GestureRecognizers.Add(tap);
    }
}
