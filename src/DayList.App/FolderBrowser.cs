using Microsoft.UI.Xaml;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace DayList;

internal readonly record struct FolderPick(string? Path, string? Error);

internal static class FolderBrowser
{
    public static async Task<FolderPick> PickAsync(Window window)
    {
        try
        {
            var picker = new FolderPicker
            {
                SuggestedStartLocation = PickerLocationId.DocumentsLibrary
            };
            picker.FileTypeFilter.Add("*");
            InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(window));
            var folder = await picker.PickSingleFolderAsync();
            return new FolderPick(folder?.Path, null);
        }
        catch (Exception ex)
        {
            return new FolderPick(null, "无法打开文件夹选择器，可以把路径粘贴到输入框。" + ex.Message);
        }
    }
}
