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
            for (int i = 0; i < parts.Length; i++)
            {
                if (IsMapFolderName(parts[i]))
                    return string.Join("/", parts, 0, i + 1);
            }

            return "";
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
            return parts.Length > 0 && IsMapFolderName(parts[parts.Length - 1]);
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
