using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace LocalIA.App.Controls;

public partial class AiActionIcon : UserControl
{
    public static readonly DependencyProperty IsBusyProperty = DependencyProperty.Register(
        nameof(IsBusy), typeof(bool), typeof(AiActionIcon), new PropertyMetadata(false, OnIsBusyChanged));

    public bool IsBusy
    {
        get => (bool)GetValue(IsBusyProperty);
        set => SetValue(IsBusyProperty, value);
    }

    public AiActionIcon() => InitializeComponent();

    private static void OnIsBusyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var icon = (AiActionIcon)d;
        var busy = (bool)e.NewValue;
        icon.RobotGlyph.Visibility = busy ? Visibility.Collapsed : Visibility.Visible;
        icon.Spinner.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;

        if (busy)
        {
            icon.SpinnerRotate.BeginAnimation(RotateTransform.AngleProperty, new DoubleAnimation(0, 360, TimeSpan.FromSeconds(0.9))
            {
                RepeatBehavior = RepeatBehavior.Forever,
            });
        }
        else
        {
            // Enlève l'animation plutôt que de la laisser tourner sur un Path masqué : sinon elle
            // continue à consommer un tick de composition à chaque frame indéfiniment en arrière-plan.
            icon.SpinnerRotate.BeginAnimation(RotateTransform.AngleProperty, null);
        }
    }
}
