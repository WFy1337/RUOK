using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Windows.Storage.Pickers;
using RUOK_App.Resources;

namespace RUOK_App.Services;

public interface IUserDialogs
{
    Task<bool> ConfirmAsync(string title, string message, string action, CancellationToken cancellationToken);
    Task<string?> ChooseCsvPathAsync(CancellationToken cancellationToken);
}

public sealed class UserDialogs(Window window) : IUserDialogs
{
    public async Task<bool> ConfirmAsync(
        string title, string message, string action, CancellationToken cancellationToken)
    {
        if (window.Content is not FrameworkElement { XamlRoot: not null } content)
            throw new InvalidOperationException("The window is not ready to display a dialog.");
        var dialog = new ContentDialog
        {
            XamlRoot = content.XamlRoot,
            RequestedTheme = content.ActualTheme,
            Title = title,
            Content = new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap, MaxWidth = 480 },
            PrimaryButtonText = action,
            CloseButtonText = UiText.Get("Cancel"),
            DefaultButton = ContentDialogButton.Close
        };
        return await dialog.ShowAsync().AsTask(cancellationToken) == ContentDialogResult.Primary;
    }

    public async Task<string?> ChooseCsvPathAsync(CancellationToken cancellationToken)
    {
        var picker = new FileSavePicker(window.AppWindow.Id)
        {
            SuggestedFileName = $"RUOK-{DateTime.Today:yyyy-MM-dd}",
            DefaultFileExtension = ".csv",
            ShowOverwritePrompt = true
        };
        picker.FileTypeChoices.Add(UiText.Get("CsvFile"), new List<string> { ".csv" });
        var result = await picker.PickSaveFileAsync().AsTask(cancellationToken);
        return result?.Path;
    }
}
