namespace FuniturenEditor.Desktop;

using System.Windows.Controls;
using System.Windows.Media;

public enum PasswordStrength
{
    None,
    Weak,
    Medium,
    Strong
}

public static class PasswordStrengthEvaluator
{
    public static PasswordStrength Evaluate(string password)
    {
        if (password.Length == 0)
            return PasswordStrength.None;

        var characterGroups = 0;
        if (password.Any(char.IsLower)) characterGroups++;
        if (password.Any(char.IsUpper)) characterGroups++;
        if (password.Any(char.IsDigit)) characterGroups++;
        if (password.Any(character => !char.IsLetterOrDigit(character))) characterGroups++;

        if (password.Length >= 12 && characterGroups >= 3)
            return PasswordStrength.Strong;
        if (password.Length >= 8 || characterGroups >= 3)
            return PasswordStrength.Medium;
        return PasswordStrength.Weak;
    }
}

public static class PasswordStrengthDisplay
{
    private static readonly Brush InactiveBrush = new SolidColorBrush(Color.FromRgb(216, 224, 232));

    public static void Update(
        string password,
        TextBlock label,
        Border first,
        Border second,
        Border third)
    {
        var strength = PasswordStrengthEvaluator.Evaluate(password);
        var (text, color, activeSegments) = strength switch
        {
            PasswordStrength.Weak => ("Schwach", Color.FromRgb(210, 55, 50), 1),
            PasswordStrength.Medium => ("Mittel", Color.FromRgb(220, 158, 25), 2),
            PasswordStrength.Strong => ("Stark", Color.FromRgb(37, 145, 83), 3),
            _ => ("Kein Passwort", Color.FromRgb(106, 119, 133), 0)
        };

        label.Text = text;
        label.Foreground = new SolidColorBrush(color);
        var activeBrush = new SolidColorBrush(color);
        first.Background = activeSegments >= 1 ? activeBrush : InactiveBrush;
        second.Background = activeSegments >= 2 ? activeBrush : InactiveBrush;
        third.Background = activeSegments >= 3 ? activeBrush : InactiveBrush;
    }
}
