using DayList.Core;
using Microsoft.UI.Xaml;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace DayList;

internal static class FileBrowser
{
    public static async Task<IReadOnlyList<string>> PickAttachmentsAsync(Window window)
    {
        var picker = new FileOpenPicker
        {
            SuggestedStartLocation = PickerLocationId.DocumentsLibrary,
            ViewMode = PickerViewMode.Thumbnail
        };
        foreach (var filter in AttachmentKinds.PickerFilters)
            picker.FileTypeFilter.Add(filter);

        InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(window));
        var files = await picker.PickMultipleFilesAsync();
        if (files is null || files.Count == 0)
            return Array.Empty<string>();
        return files.Select(file => file.Path).ToArray();
    }
}
