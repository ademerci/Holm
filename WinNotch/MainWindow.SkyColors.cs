using System.Windows;

namespace WinNotch;

// The sky colours of the open notch can be switched off: then it stays plain black, with the greeting in the usual grey.
public partial class MainWindow
{
    void MenuSkyColors_Click(object sender, RoutedEventArgs e)
    {
        _settings.SkyColors = MenuSkyColors.IsChecked;
        _settings.Save();
        _skyShown = null;
        ShowSky();
    }
}