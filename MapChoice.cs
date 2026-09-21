using FrostySdk.Managers;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace NoHavokSplitPlugin
{
    public class MapChoice : INotifyPropertyChanged
    {
        bool isChecked;

        public string Key { get; set; }
        public string DisplayName { get; set; }
        public int SubWorldCount => SubWorlds.Count;
        public List<EbxAssetEntry> SubWorlds { get; } = new List<EbxAssetEntry>();

        public bool IsChecked
        {
            get => isChecked;
            set
            {
                if (isChecked == value)
                    return;
                isChecked = value;
                OnPropertyChanged();
            }
        }

        public string ListText => $"{DisplayName}  ({SubWorldCount} SubWorlds)";

        public event PropertyChangedEventHandler PropertyChanged;

        void OnPropertyChanged([CallerMemberName] string name = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }

        public static string GetMapKey(string assetName)
        {
            if (string.IsNullOrEmpty(assetName))
                return "";

            string[] parts = assetName.Replace('\\', '/').Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
            int levelsIdx = -1;
            for (int i = 0; i < parts.Length; i++)
            {
                if (string.Equals(parts[i], "Levels", StringComparison.OrdinalIgnoreCase))
                {
                    levelsIdx = i;
                    break;
                }
            }

            if (levelsIdx < 0)
            {
                if (parts.Length >= 2)
                    return parts[0] + "/" + parts[1];
                return parts.Length > 0 ? parts[0] : assetName;
            }

            int last = Math.Min(parts.Length - 1, levelsIdx + 2);
            return string.Join("/", parts, 0, last + 1);
        }

        public static string GetDisplayName(string mapKey)
        {
            if (string.IsNullOrEmpty(mapKey))
                return mapKey;

            string[] parts = mapKey.Replace('\\', '/').Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0)
                return mapKey;

            string map = parts[parts.Length - 1];
            if (parts.Length >= 2 && !string.Equals(parts[0], "Levels", StringComparison.OrdinalIgnoreCase))
                return map + "  [" + parts[0] + "]";
            return map;
        }

        public static bool IsRealMap(string mapKey)
        {
            if (string.IsNullOrEmpty(mapKey))
                return false;

            string[] parts = mapKey.Replace('\\', '/').Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 2)
                return false;

            string folder = parts[parts.Length - 2];
            string map = parts[parts.Length - 1];
            if (!string.Equals(folder, "MP", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(folder, "SP", StringComparison.OrdinalIgnoreCase))
                return false;

            return IsMapFolderName(map);
        }

        static bool IsMapFolderName(string name)
        {
            if (string.IsNullOrEmpty(name))
                return false;
            if (string.Equals(name, "MP", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(name, "SP", StringComparison.OrdinalIgnoreCase))
                return false;

            if (name.StartsWith("MP_", StringComparison.OrdinalIgnoreCase) ||
                name.StartsWith("SP_", StringComparison.OrdinalIgnoreCase))
                return true;

            if (name.Length > 2 &&
                (name.StartsWith("MP", StringComparison.OrdinalIgnoreCase) ||
                 name.StartsWith("SP", StringComparison.OrdinalIgnoreCase)) &&
                char.IsLetter(name[2]))
                return true;

            return false;
        }
    }
}
