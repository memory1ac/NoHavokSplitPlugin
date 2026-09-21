using Frosty.Core.Attributes;
using NoHavokSplitPlugin;
using System.Runtime.InteropServices;
using System.Windows;

[assembly: ComVisible(false)]

[assembly: ThemeInfo(
    ResourceDictionaryLocation.None,
    ResourceDictionaryLocation.SourceAssembly)]

[assembly: Guid("B4E71C20-9A58-4D3F-8C12-6F90E4A1B277")]

[assembly: PluginDisplayName("NoHavok Split StaticModelGroup")]
[assembly: PluginAuthor("BF1AI")]
[assembly: PluginVersion("1.0.0.0")]

[assembly: RegisterMenuExtension(typeof(NoHavokSplitMenuExtension))]
