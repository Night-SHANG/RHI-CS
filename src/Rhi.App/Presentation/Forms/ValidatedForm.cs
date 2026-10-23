using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Automation;
using Rhi.App.Localization;
using Rhi.Core.Models;

namespace Rhi.App.Presentation;

public sealed partial class PageContext
{
    public async Task<bool> Edit(string title, StackPanel fields, Action validate, Func<Task> submit,
        Control[] inputs, string action = "Save")
    {
        var error = Ui.Text("");
        AutomationProperties.SetAutomationId(error, "FormValidation");
        error.Visibility = Visibility.Collapsed;
        error.Margin = new Thickness(0, 8, 0, 0);
        var scroll = DialogContent(fields);
        // Validation stays above the footer when the fields need scrolling.
        // Collapsed feedback contributes no space to a fresh or valid form.
        var content = new StackPanel();
        content.Children.Add(scroll);
        content.Children.Add(error);
        var dialog = new ContentDialog { XamlRoot = Root.XamlRoot, Title = Strings.Get(title),
            Content = content, PrimaryButtonText = Strings.Get("Cancel"),
            SecondaryButtonText = Strings.Get(action), DefaultButton = ContentDialogButton.Secondary,
            RequestedTheme = Root.RequestedTheme };
        AutomationProperties.SetAutomationId(dialog, "ValidatedForm");
        TrackDialogSize(dialog, scroll, feedback: error);
        void Feedback(string text)
        {
            error.Text = text;
            error.Visibility = text.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
        }
        var submitting = false;
        var saved = false;
        var attempted = false;
        string ErrorText(Exception ex) => ex is DeploymentException de ? Strings.Get(de.Code) : Strings.Get("InspectionFailed") + "\n" + ex.Message;
        void Check()
        {
            if (submitting) return;
            // All editors share the same submission boundary; opening a blank form is not an error.
            if (!attempted) { Feedback(""); dialog.IsSecondaryButtonEnabled = true; return; }
            try { validate(); Feedback(""); error.Foreground = Ui.ThemeBrush("SecondaryTextBrush"); dialog.IsSecondaryButtonEnabled = true; }
            catch (Exception ex) when (ex is DeploymentException or IOException or UnauthorizedAccessException or ArgumentException)
            {
                Feedback(ErrorText(ex));
                error.Foreground = Ui.ThemeBrush("ErrorBrush");
                dialog.IsSecondaryButtonEnabled = false;
            }
        }
        foreach (var input in inputs)
        {
            if (input is TextBox text) text.TextChanged += (_, _) => Check();
            if (input is ComboBox choice) choice.SelectionChanged += (_, _) => Check();
        }
        dialog.SecondaryButtonClick += async (_, args) =>
        {
            var deferral = args.GetDeferral();
            args.Cancel = true;
            submitting = true;
            attempted = true;
            dialog.IsPrimaryButtonEnabled = false; dialog.IsSecondaryButtonEnabled = false;
            var enabled = inputs.ToDictionary(input => input, input => input.IsEnabled);
            foreach (var input in inputs) input.IsEnabled = false;
            Feedback(Strings.Get("FormChecking"));
            error.Foreground = Ui.ThemeBrush("SecondaryTextBrush");
            var valid = false;
            try { validate(); valid = true; await submit(); saved = true; args.Cancel = false; }
            catch (Exception ex) { Feedback(ErrorText(ex)); error.Foreground = Ui.ThemeBrush("ErrorBrush"); }
            finally
            {
                foreach (var input in inputs) input.IsEnabled = enabled[input];
                submitting = false;
                dialog.IsPrimaryButtonEnabled = true;
                dialog.IsSecondaryButtonEnabled = valid;
                deferral.Complete();
            }
        };
        Check();
        await dialog.ShowAsync();
        return saved;
    }
}
