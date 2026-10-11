using System.Windows;
using System.Windows.Controls;

namespace GuessMelody.Views.Tabs
{
    public partial class SettingsTab : UserControl
    {
        public SettingsTab()
        {
            InitializeComponent();
            if (SectionsList != null && SectionsList.Items.Count > 0)
                SectionsList.SelectedIndex = 0;
        }

        private void SectionsList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!(SectionsList.SelectedItem is ListBoxItem item)) return;
            var tag = item.Tag as string;

            if (PanelGame == null || PanelAudio == null || PanelTimings == null ||
                PanelAppearance == null || PanelPresets == null) return;

            PanelGame.Visibility = tag == "game" ? Visibility.Visible : Visibility.Collapsed;
            PanelAudio.Visibility = tag == "audio" ? Visibility.Visible : Visibility.Collapsed;
            PanelTimings.Visibility = tag == "timings" ? Visibility.Visible : Visibility.Collapsed;
            PanelAppearance.Visibility = tag == "appearance" ? Visibility.Visible : Visibility.Collapsed;
            PanelPresets.Visibility = tag == "presets" ? Visibility.Visible : Visibility.Collapsed;
        }
    }
}