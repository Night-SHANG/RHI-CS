using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Markup;
using Rhi.App.Localization;

namespace Rhi.App.Presentation;

public static partial class Ui
{
    public static Expander FileDetails(string id, int count, Func<IEnumerable<string>> rows)
    {
        var expander = new Expander { Header = Strings.Format("FileDetailsCount", count),
            HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch };
        AutomationProperties.SetAutomationId(expander, "FileDetails_" + id);
        expander.Expanding += (_, _) =>
        {
            if (expander.Content is not null) return;
            // A bounded ListView keeps its native recycling panel. Do not materialize UIElements for every file.
            var list = new ListView { ItemsSource = rows().ToArray(), Height = 200,
                SelectionMode = ListViewSelectionMode.None, IsItemClickEnabled = false,
                HorizontalContentAlignment = HorizontalAlignment.Stretch,
                ItemTemplate = (DataTemplate)XamlReader.Load("<DataTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation'><TextBlock Text='{Binding}' TextWrapping='Wrap' Margin='0,4'/></DataTemplate>") };
            AutomationProperties.SetAutomationId(list, "FileList_" + id);
            expander.Content = list;
        };
        return expander;
    }
}
