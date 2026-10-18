using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Automation;
using Rhi.App.Localization;
using Rhi.Core.Models;

namespace Rhi.App.Presentation;

public sealed partial class PageContext
{
    public async Task<bool> Edit(string title, StackPanel fields, Action validate, Func<Task> submit,
        Control[] inputs, string action = "Save", bool deferNameError = false)
    {
        var error = Ui.Text("");
        AutomationProperties.SetAutomationId(error, "FormValidation");
        error.MinHeight = 58;
        var scroll = DialogContent(fields);
        scroll.Height = Math.Clamp(Root.ActualHeight - 324, 64, 360);
        var feedback = new ScrollViewer { Content = error, Height = 66,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        var dialog = new ContentDialog { XamlRoot = Root.XamlRoot, Title = Strings.Get(title),
            Content = Ui.Stack(scroll, feedback), PrimaryButtonText = Strings.Get("Cancel"),
            SecondaryButtonText = Strings.Get(action), DefaultButton = ContentDialogButton.Secondary,
            RequestedTheme = Root.RequestedTheme };
        AutomationProperties.SetAutomationId(dialog, "ValidatedForm");
        TrackDialogSize(dialog, scroll, feedback);
        var submitting = false;
        var saved = false;
        var attempted = false;
        string ErrorText(Exception ex) => ex is DeploymentException de ? Strings.Get(de.Code) : Strings.Get("InspectionFailed") + "\n" + ex.Message;
        void Check()
        {
            if (submitting) return;
            try { validate(); error.Text = ""; error.Foreground = Ui.ThemeBrush("SecondaryTextBrush"); dialog.IsSecondaryButtonEnabled = true; }
            catch (Exception ex) when (ex is DeploymentException or IOException or UnauthorizedAccessException or ArgumentException)
            {
                var defer = deferNameError && !attempted && ex is DeploymentException { Code: "NameRequired" };
                error.Text = defer ? "" : ErrorText(ex);
                error.Foreground = Ui.ThemeBrush("ErrorBrush");
                dialog.IsSecondaryButtonEnabled = defer;
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
            error.Text = Strings.Get("FormChecking");
            error.Foreground = Ui.ThemeBrush("SecondaryTextBrush");
            try { validate(); await submit(); saved = true; args.Cancel = false; }
            catch (Exception ex) { error.Text = ErrorText(ex); error.Foreground = Ui.ThemeBrush("ErrorBrush"); }
            finally
            {
                foreach (var input in inputs) input.IsEnabled = enabled[input];
                submitting = false;
                dialog.IsPrimaryButtonEnabled = true;
                dialog.IsSecondaryButtonEnabled = true;
                deferral.Complete();
            }
        };
        Check();
        await dialog.ShowAsync();
        return saved;
    }
}
