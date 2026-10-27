using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using AutoTestRunner.Running;

namespace AutoTestRunner.Cli.Views;

internal sealed partial class RunPanel
{
    // Shows a question above the feed, with a button for each answer, and gives the index of the one clicked. Closing
    // the panel gives closeAnswer, the last by default.
    private Task<int> Ask(Brush color, string glyph, string title, string message, (string Glyph, string Text)[] answers, string? screenshot = null,
        int? closeAnswer = null)
    {
        Answer(_closeAnswer);
        EndWait();
        var answer = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        (_answer, _closeAnswer) = (answer, closeAnswer ?? answers.Length - 1);

        var stack = new StackPanel();
        var heading = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 4) };
        heading.Children.Add(Ui.Glyph(glyph, 16, color));
        var words = Ui.Words(title, 14, color, FontWeights.Bold);
        words.Margin = new Thickness(8, 0, 0, 0);
        heading.Children.Add(words);
        stack.Children.Add(heading);
        stack.Children.Add(new TextBox
        {
            Text = message, IsReadOnly = true, BorderThickness = new Thickness(0), Background = Brushes.Transparent, TextWrapping = TextWrapping.Wrap,
            FontSize = 12.5, Foreground = Ui.Ink, MaxHeight = 160, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Padding = new Thickness(-2, 0, 0, 0),
        });
        if (screenshot is not null && File.Exists(screenshot))
            stack.Children.Add(Thumbnail(screenshot));
        var buttons = new WrapPanel { Margin = new Thickness(0, 10, 0, 0) };
        for (var index = 0; index < answers.Length; index++)
        {
            var picked = index;
            buttons.Children.Add(Ui.LabelledButton(answers[index].Glyph, answers[index].Text, () => Answer(picked), index == 0 ? "MainButton" : "PlainButton"));
        }
        stack.Children.Add(buttons);
        _question.Content = new Border
        {
            Child = stack, BorderBrush = color, BorderThickness = new Thickness(3, 0, 0, 0), CornerRadius = new CornerRadius(4),
            Background = color == Ui.Fail ? Ui.FailSoft : Ui.Surface, Padding = new Thickness(12, 10, 12, 4), Margin = new Thickness(0, 6, 0, 0),
        };
        System.Media.SystemSounds.Exclamation.Play();
        return answer.Task;
    }

    /// <summary>Answers the question on show, as clicking its answer does.</summary>
    internal void Answer(int index)
    {
        if (_answer is not { } answer)
            return;
        _answer = null;
        _question.Content = null;
        answer.TrySetResult(index);
    }

    // The screenshot it saved, small; a click opens it.
    private Image Thumbnail(string path)
    {
        var picture = new System.Windows.Media.Imaging.BitmapImage();
        picture.BeginInit();
        picture.UriSource = new Uri(path);
        picture.DecodePixelWidth = 400;
        picture.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
        picture.EndInit();
        var image = new Image { Source = picture, MaxHeight = 120, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 8, 0, 0), Cursor = System.Windows.Input.Cursors.Hand, ToolTip = path };
        image.MouseLeftButtonUp += (_, _) => Open("app", path);
        return image;
    }

    private Border FinishedCard(string resultsFolder, bool allPassed)
    {
        var stack = new StackPanel();
        var counts = new WrapPanel { Margin = new Thickness(0, 0, 0, 8) };
        foreach (var status in Enum.GetValues<CaseStatus>())
        {
            var count = _ended.Count(result => result.Status == status);
            if (count == 0 && status != CaseStatus.Passed)
                continue;
            var pill = Ui.Pill(Ui.IconText(Icons.Of(status), $"{count}", Ui.Of(status), 13), Brushes.White, 12, new Thickness(10, 4, 12, 4));
            pill.Margin = new Thickness(0, 0, 6, 0);
            pill.ToolTip = status.ToString();
            counts.Children.Add(pill);
        }
        var took = Ui.Words(Ui.Took(TimeSpan.FromTicks(_ended.Sum(result => result.Duration.Ticks))), 12, Ui.Muted, wrap: false);
        took.Margin = new Thickness(4, 0, 0, 0);
        counts.Children.Add(took);
        stack.Children.Add(counts);
        var buttons = new WrapPanel();
        if (_canRunAgain)
            buttons.Children.Add(Ui.LabelledButton(Icons.Retry, "Run more", () =>
            {
                RunAgain = true;
                Close();
            }, "MainButton"));
        buttons.Children.Add(Ui.LabelledButton(Icons.Folder, "Results", () => Open("folder", resultsFolder)));
        buttons.Children.Add(Ui.LabelledButton(Icons.Document, "Summary", () => Open("file", Path.Combine(resultsFolder, "summary.txt"))));
        buttons.Children.Add(Ui.LabelledButton(Icons.Cross, "Close", Close));
        stack.Children.Add(buttons);
        return new Border
        {
            Child = stack, BorderBrush = allPassed ? Ui.Pass : Ui.Fail, BorderThickness = new Thickness(3, 0, 0, 0), CornerRadius = new CornerRadius(4),
            Background = allPassed ? Ui.PassSoft : Ui.FailSoft, Padding = new Thickness(12, 10, 12, 4), Margin = new Thickness(0, 6, 0, 0),
        };
    }
}
