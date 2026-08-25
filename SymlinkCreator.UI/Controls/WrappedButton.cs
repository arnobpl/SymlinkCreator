using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace SymlinkCreator.Controls;

public sealed partial class WrappedButton : Button
{
    public static readonly DependencyProperty TextProperty = DependencyProperty.Register(
        nameof(Text),
        typeof(string),
        typeof(WrappedButton),
        new PropertyMetadata(string.Empty, static (dependencyObject, args) =>
        {
            ((WrappedButton)dependencyObject)._label.Text = (string?)args.NewValue ?? string.Empty;
        }));

    public static readonly DependencyProperty LeadingContentProperty = DependencyProperty.Register(
        nameof(LeadingContent),
        typeof(UIElement),
        typeof(WrappedButton),
        new PropertyMetadata(null, static (dependencyObject, args) =>
        {
            ((WrappedButton)dependencyObject)._leadingContentPresenter.Content = args.NewValue;
        }));

    private readonly ContentPresenter _leadingContentPresenter;
    private readonly TextBlock _label;

    public WrappedButton()
    {
        HorizontalContentAlignment = HorizontalAlignment.Stretch;
        VerticalContentAlignment = VerticalAlignment.Center;

        _leadingContentPresenter = new ContentPresenter
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        _label = new TextBlock
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Center,
            TextAlignment = TextAlignment.Center,
            TextWrapping = TextWrapping.Wrap
        };

        var content = new Grid { ColumnSpacing = 8 };
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        content.Children.Add(_leadingContentPresenter);
        content.Children.Add(_label);
        Grid.SetColumn(_label, 1);

        Content = content;
    }

    public string Text
    {
        get => (string)GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    public UIElement? LeadingContent
    {
        get => (UIElement?)GetValue(LeadingContentProperty);
        set => SetValue(LeadingContentProperty, value);
    }
}
