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
        error.MinHeight = 58;
        var scroll = DialogContent(fields);
        scroll.Height = Math.Clamp(Root.ActualHeight - 290, 140, 380);
        var feedback = new ScrollViewer { Content = error, Height = 66,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        var dialog = new ContentDialog { XamlRoot = Root.XamlRoot, Title = Strings.Get(title),
            Content = Ui.Stack(scroll, feedback), PrimaryButtonText = Strings.Get("Cancel"),
            SecondaryButtonText = Strings.Get(action), DefaultButton = ContentDialogButton.Secondary,
            RequestedTheme = Root.RequestedTheme };
        AutomationProperties.SetAutomationId(dialog, "ValidatedForm");
        var submitting = false;
        var saved = false;
        string ErrorText(Exception ex) => ex is DeploymentException de ? Strings.Get(de.Code) : Strings.Get("InspectionFailed") + "\n" + ex.Message;
        void Check()
        {
            if (submitting) return;
            try { validate(); error.Text = Strings.Get("FormReady"); dialog.IsSecondaryButtonEnabled = true; }
            catch (Exception ex) when (ex is DeploymentException or IOException or UnauthorizedAccessException or ArgumentException)
            { error.Text = ErrorText(ex); dialog.IsSecondaryButtonEnabled = false; }
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
            dialog.IsPrimaryButtonEnabled = false; dialog.IsSecondaryButtonEnabled = false;
            var enabled = inputs.ToDictionary(input => input, input => input.IsEnabled);
            foreach (var input in inputs) input.IsEnabled = false;
            error.Text = Strings.Get("FormChecking");
            try { validate(); await submit(); saved = true; args.Cancel = false; }
            catch (Exception ex) { error.Text = ErrorText(ex); }
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
