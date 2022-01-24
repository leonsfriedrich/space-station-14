using Content.Client.Administration;
using Robust.Client.AutoGenerated;
using Robust.Client.Console;
using Robust.Client.UserInterface.Controls;
using Robust.Client.UserInterface.CustomControls;
using Robust.Client.UserInterface.XAML;
using Robust.Shared.GameObjects;

namespace Content.Client.EscapeMenu.UI
{
    [GenerateTypedNameReferences]
    internal partial class EscapeMenu : DefaultWindow
    {
        private readonly IClientConsoleHost _consoleHost;

        private readonly OptionsMenu _optionsMenu;

        public EscapeMenu(IClientConsoleHost consoleHost)
        {
            _consoleHost = consoleHost;

            RobustXamlLoader.Load(this);

            _optionsMenu = new OptionsMenu();

            OptionsButton.OnPressed += OnOptionsButtonClicked;
            QuitButton.OnPressed += OnQuitButtonClicked;
            AHelpButton.OnPressed += OnAHelpButtonClicked;
            DisconnectButton.OnPressed += OnDisconnectButtonClicked;
        }

        private void OnQuitButtonClicked(BaseButton.ButtonEventArgs args)
        {
            _consoleHost.ExecuteCommand("quit");
            Dispose();
        }

        private void OnAHelpButtonClicked(BaseButton.ButtonEventArgs args)
        {
            _consoleHost.ExecuteCommand("openahelp");
            // Doing Dispose() here causes issues because you can't un-dispose the escape menu.
            // The other commands don't really suffer as much from it. Unsure if bug.
        }

        private void OnDisconnectButtonClicked(BaseButton.ButtonEventArgs args)
        {
            _consoleHost.ExecuteCommand("disconnect");
            Dispose();
        }

        private void OnOptionsButtonClicked(BaseButton.ButtonEventArgs args)
        {
            _optionsMenu.OpenCentered();
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);

            if (disposing)
            {
                _optionsMenu.Dispose();
            }
        }
    }
}
