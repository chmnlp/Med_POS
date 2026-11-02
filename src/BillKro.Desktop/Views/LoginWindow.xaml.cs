using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using BillKro.Desktop.ViewModels;

namespace BillKro.Desktop.Views;

public partial class LoginWindow : Window
{
    private readonly LoginViewModel _vm;

    /// <summary>Respect "Animation effects" being switched off in Windows settings.</summary>
    private static bool Animate => SystemParameters.ClientAreaAnimation;

    public LoginWindow(LoginViewModel vm)
    {
        InitializeComponent();
        DataContext = _vm = vm;
        vm.SignedIn += async () =>
        {
            await PlayExitAsync();
            DialogResult = true;
        };

        ErrorBanner.IsVisibleChanged += (_, e) =>
        {
            if (e.NewValue is true) Reveal(ErrorBanner, 0, 0, -8, 260);
        };

        Loaded += (_, _) =>
        {
            PlayEntrance();
            if (string.IsNullOrEmpty(vm.Username)) UsernameBox.Focus();
            else PasswordBox.Focus();
        };
    }

    private async void OnSignIn(object sender, RoutedEventArgs e)
    {
        await _vm.SignInAsync(PasswordBox.Password);
        if (_vm.ErrorMessage is null) return;

        // Connection problems are most often a wrong server address; surface that field.
        if (_vm.ErrorMessage.StartsWith("Cannot reach") || _vm.ErrorMessage.StartsWith("Server address"))
            ServerExpander.IsExpanded = true;
        Shake();
        if (_revealed) { PasswordText.Focus(); PasswordText.SelectAll(); }
        else { PasswordBox.Focus(); PasswordBox.SelectAll(); }
    }

    private bool _revealed;

    /// <summary>Eye button: swap between the masked box and a plain-text copy of the password.</summary>
    private void OnRevealClick(object sender, RoutedEventArgs e)
    {
        _revealed = !_revealed;
        if (_revealed)
        {
            PasswordText.Text = PasswordBox.Password;
            PasswordText.Visibility = Visibility.Visible;
            PasswordBox.Visibility = Visibility.Collapsed;
            PasswordText.Focus();
            PasswordText.CaretIndex = PasswordText.Text.Length;
        }
        else
        {
            PasswordBox.Password = PasswordText.Text;
            PasswordBox.Visibility = Visibility.Visible;
            PasswordText.Visibility = Visibility.Collapsed;
            PasswordText.Clear();
            PasswordBox.Focus();
        }
        RevealGlyph.Text = ((char)(_revealed ? 0xED1A : 0xE7B3)).ToString();   // Segoe Fluent icons: Hide / View
        var tip = _revealed ? "Hide password" : "Show password";
        RevealButton.ToolTip = tip;
        System.Windows.Automation.AutomationProperties.SetName(RevealButton, tip);
    }

    // Typing in the visible box keeps the real PasswordBox (used for sign-in) up to date.
    private void OnPasswordTextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        if (_revealed) PasswordBox.Password = PasswordText.Text;
    }

    private void PlayEntrance()
    {
        if (!Animate) return;

        // Brand panel: logo pops in, headline and features rise in one after another.
        var pop = new ScaleTransform(0.4, 0.4);
        LogoTile.RenderTransform = pop;
        var back = new BackEase { Amplitude = 0.5, EasingMode = EasingMode.EaseOut };
        pop.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(0.4, 1, Ms(650)) { EasingFunction = back });
        pop.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(0.4, 1, Ms(650)) { EasingFunction = back });

        Reveal(LogoRow, 0, -16, 0);
        Reveal(HeadLine1, 150, 0, 22);
        Reveal(HeadLine2, 260, 0, 22);
        Reveal(Feature1, 420, -24, 0);
        Reveal(Feature2, 520, -24, 0);
        Reveal(Feature3, 620, -24, 0);
        Reveal(Footer, 800, 0, 0, 700);

        // Form: each row slides in from the right, slightly after the one above.
        var delay = 200;
        foreach (UIElement child in FormPanel.Children)
        {
            if (child is FrameworkElement fe && fe != ErrorBanner)
                Reveal(fe, delay, 28, 0);
            delay += 55;
        }

        Begin("AmbientMotion");
        Begin("LogoPulse");
        Begin("HeadlineShine");
    }

    private void Begin(string key) => ((Storyboard)Resources[key]).Begin(this, true);

    private static void Reveal(FrameworkElement el, int delayMs, double fromX, double fromY, int durationMs = 520)
    {
        if (!Animate) return;
        var begin = TimeSpan.FromMilliseconds(delayMs);
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        var move = new TranslateTransform(fromX, fromY);
        el.RenderTransform = move;
        el.Opacity = 0;

        el.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, Ms(durationMs)) { BeginTime = begin, EasingFunction = ease });
        if (fromX != 0)
            move.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(fromX, 0, Ms(durationMs)) { BeginTime = begin, EasingFunction = ease });
        if (fromY != 0)
            move.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(fromY, 0, Ms(durationMs)) { BeginTime = begin, EasingFunction = ease });
    }

    /// <summary>Quick side-to-side shake of the form after a failed sign-in.</summary>
    private void Shake()
    {
        if (!Animate) return;
        var shake = new DoubleAnimationUsingKeyFrames { Duration = Ms(420) };
        double[] offsets = [0, -12, 10, -8, 6, -3, 0];
        for (var i = 0; i < offsets.Length; i++)
            shake.KeyFrames.Add(new EasingDoubleKeyFrame(offsets[i], KeyTime.FromTimeSpan(Ms(i * 70))));
        FormShake.BeginAnimation(TranslateTransform.XProperty, shake);
    }

    private Task PlayExitAsync()
    {
        if (!Animate) return Task.CompletedTask;
        var done = new TaskCompletionSource();
        var ease = new CubicEase { EasingMode = EasingMode.EaseIn };
        var fade = new DoubleAnimation(1, 0, Ms(240)) { EasingFunction = ease };
        fade.Completed += (_, _) => done.TrySetResult();
        RootScale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(1, 0.97, Ms(240)) { EasingFunction = ease });
        RootScale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(1, 0.97, Ms(240)) { EasingFunction = ease });
        RootGrid.BeginAnimation(OpacityProperty, fade);
        return done.Task;
    }

    private void OnButtonEnter(object sender, System.Windows.Input.MouseEventArgs e) => NudgeArrow(5);

    private void OnButtonLeave(object sender, System.Windows.Input.MouseEventArgs e) => NudgeArrow(0);

    private void NudgeArrow(double to)
    {
        if (!Animate) return;
        ArrowNudge.BeginAnimation(TranslateTransform.XProperty,
            new DoubleAnimation(to, Ms(180)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
    }

    private static TimeSpan Ms(int ms) => TimeSpan.FromMilliseconds(ms);
}
