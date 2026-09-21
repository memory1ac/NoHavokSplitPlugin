using Frosty.Controls;
using Frosty.Core;
using NoHavokSplitPlugin.Windows;
using System.Windows;

namespace NoHavokSplitPlugin
{
    public class NoHavokSplitMenuExtension : MenuExtension
    {
        public override string TopLevelMenuName => "Tools";
        public override string SubLevelMenuName => null;
        public override string MenuItemName => "Split StaticModelGroups (NoHavok)";

        public override RelayCommand MenuItemClicked => new RelayCommand((o) =>
        {
            if (App.AssetManager == null)
            {
                FrostyMessageBox.Show("No game data loaded.", "NoHavok Split");
                return;
            }

            NoHavokSplitWindow.ShowDialogWindow();
        });
    }
}
