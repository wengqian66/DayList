using System.ComponentModel;
using System.Runtime.CompilerServices;
using DayList.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;

namespace DayList;

public sealed class TaskAttachmentRow : INotifyPropertyChanged
{
    private ImageSource? _thumbnail;
    private bool _imageFailed;

    public event PropertyChangedEventHandler? PropertyChanged;

    private TaskAttachmentRow(
        string relativePath,
        string displayName,
        string extensionLabel,
        bool isImage,
        string fullPath,
        string? originalToken,
        string toolTip)
    {
        RelativePath = relativePath;
        DisplayName = displayName;
        ExtensionLabel = extensionLabel;
        IsImage = isImage;
        FullPath = fullPath;
        OriginalToken = originalToken;
        ToolTip = toolTip;
    }

    public string RelativePath { get; }

    public string DisplayName { get; }

    public string ExtensionLabel { get; }

    public bool IsImage { get; }

    public string FullPath { get; }

    public string? OriginalToken { get; }

    public string ToolTip { get; }

    public ImageSource? Thumbnail
    {
        get => _thumbnail;
        private set
        {
            if (ReferenceEquals(_thumbnail, value))
                return;
            _thumbnail = value;
            Notify();
        }
    }

    public Visibility ImageSlotVisibility =>
        IsImage && !_imageFailed ? Visibility.Visible : Visibility.Collapsed;

    public Visibility ChipVisibility =>
        ImageSlotVisibility == Visibility.Visible ? Visibility.Collapsed : Visibility.Visible;

    public static TaskAttachmentRow From(TaskAttachment attachment, string? vaultPath)
    {
        var fullPath = "";
        if (!string.IsNullOrWhiteSpace(vaultPath))
        {
            try
            {
                fullPath = VaultLayout.Combine(vaultPath, attachment.RelativePath);
            }
            catch (InvalidOperationException)
            {
                fullPath = "";
            }
        }

        var extension = Path.GetExtension(attachment.RelativePath);
        var label = extension.TrimStart('.').ToUpperInvariant();
        if (label.Length == 0)
            label = "FILE";

        var row = new TaskAttachmentRow(
            attachment.RelativePath,
            string.IsNullOrWhiteSpace(attachment.DisplayName)
                ? Path.GetFileName(attachment.RelativePath)
                : attachment.DisplayName,
            label,
            attachment.IsImage,
            fullPath,
            attachment.OriginalToken,
            attachment.IsImage ? "点击预览" : "用系统默认程序打开");
        if (row.IsImage)
            row.BeginLoadThumbnail();
        return row;
    }

    public TaskAttachment ToAttachment() => new()
    {
        RelativePath = RelativePath,
        DisplayName = DisplayName,
        IsImage = IsImage,
        OriginalToken = OriginalToken
    };

    private void BeginLoadThumbnail()
    {
        var path = FullPath;
        _ = LoadThumbnailAsync(path);
    }

    private async Task LoadThumbnailAsync(string path)
    {
        var source = await AttachmentImages.LoadAsync(path, 160);
        if (source is null)
        {
            _imageFailed = true;
            Notify(nameof(ImageSlotVisibility));
            Notify(nameof(ChipVisibility));
            return;
        }

        Thumbnail = source;
    }

    private void Notify([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
