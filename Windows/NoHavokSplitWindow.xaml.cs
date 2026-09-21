using Frosty.Controls;
using Frosty.Core;
using Frosty.Core.Windows;
using FrostySdk.Managers;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace NoHavokSplitPlugin.Windows
{
    public partial class NoHavokSplitWindow : FrostyDockableWindow
    {
        List<MapChoice> allMaps = new List<MapChoice>();
        bool loaded;

        public NoHavokSplitWindow()
        {
            InitializeComponent();
        }

        public static void ShowDialogWindow()
        {
            NoHavokSplitWindow win = new NoHavokSplitWindow();
            Window owner = Application.Current?.MainWindow;
            if (owner != null && owner != win)
                win.Owner = owner;
            win.ShowDialog();
        }

        private void FrostyDockableWindow_FrostyLoaded(object sender, EventArgs e)
        {
            if (loaded)
                return;
            loaded = true;
            LoadMaps();
        }

        void LoadMaps()
        {
            Dictionary<string, MapChoice> maps = new Dictionary<string, MapChoice>(StringComparer.OrdinalIgnoreCase);
            foreach (EbxAssetEntry entry in App.AssetManager.EnumerateEbx("SubWorldData"))
            {
                string key = MapChoice.GetMapKey(entry.Name);
                if (string.IsNullOrEmpty(key) || !MapChoice.IsRealMap(key))
                    continue;

                if (!maps.TryGetValue(key, out MapChoice choice))
                {
                    choice = new MapChoice
                    {
                        Key = key,
                        DisplayName = MapChoice.GetDisplayName(key)
                    };
                    maps[key] = choice;
                }
                choice.SubWorlds.Add(entry);
            }

            allMaps = maps.Values
                .OrderBy(m => m.DisplayName, StringComparer.OrdinalIgnoreCase)
                .ToList();

            ApplyFilter();
        }

        void ApplyFilter()
        {
            string filter = filterTextBox?.Text?.Trim() ?? "";
            IEnumerable<MapChoice> view = allMaps;
            if (!string.IsNullOrEmpty(filter))
                view = allMaps.Where(m =>
                    m.DisplayName.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0 ||
                    m.Key.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0);

            mapListBox.ItemsSource = view.ToList();
            UpdateStatus();
        }

        void UpdateStatus()
        {
            int checkedMaps = allMaps.Count(m => m.IsChecked);
            int subWorlds = allMaps.Where(m => m.IsChecked).Sum(m => m.SubWorldCount);
            statusText.Text = $"已选 {checkedMaps} 张地图 / {subWorlds} 个 SubWorld";
        }

        private void MapCheck_Changed(object sender, RoutedEventArgs e)
        {
            UpdateStatus();
        }

        private void FilterTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            ApplyFilter();
        }

        private void SelectAll_Click(object sender, RoutedEventArgs e)
        {
            foreach (object item in mapListBox.Items)
            {
                if (item is MapChoice map)
                    map.IsChecked = true;
            }
            UpdateStatus();
        }

        private void SelectNone_Click(object sender, RoutedEventArgs e)
        {
            foreach (MapChoice map in allMaps)
                map.IsChecked = false;
            UpdateStatus();
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void Split_Click(object sender, RoutedEventArgs e)
        {
            List<EbxAssetEntry> selected = new List<EbxAssetEntry>();
            foreach (MapChoice map in allMaps)
            {
                if (map.IsChecked)
                    selected.AddRange(map.SubWorlds);
            }

            if (selected.Count == 0)
            {
                FrostyMessageBox.Show("请先勾选至少一张地图。", "NoHavok Split");
                return;
            }

            MessageBoxResult confirm = FrostyMessageBox.Show(
                $"将对 {allMaps.Count(m => m.IsChecked)} 张地图、{selected.Count} 个 SubWorld 拆解 StaticModelGroup。\n\n继续？",
                "NoHavok Split",
                MessageBoxButton.YesNo);
            if (confirm != MessageBoxResult.Yes)
                return;

            StaticModelGroupSplitter.SplitResult result = null;
            FrostyTaskWindow.Show("Splitting StaticModelGroups", "Preparing...", (task) =>
            {
                result = StaticModelGroupSplitter.SplitSubWorlds(selected, task);
            });

            if (result == null)
            {
                FrostyMessageBox.Show("Split did not complete.", "NoHavok Split");
                return;
            }

            FrostyMessageBox.Show(
                $"SubWorlds scanned: {result.SubWorldsScanned}\n" +
                $"SubWorlds modified: {result.SubWorldsModified}\n" +
                $"Groups removed: {result.GroupsRemoved}\n" +
                $"Existing WorldParts updated: {result.WorldPartsTouched}\n" +
                $"ObjectReferences added: {result.InstancesCreated}\n" +
                $"Skipped members: {result.MembersSkipped}\n" +
                result.WarningText,
                "NoHavok Split");
        }
    }
}
