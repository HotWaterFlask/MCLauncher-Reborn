using System.Collections.ObjectModel;
using MCLauncher.Core.Network;

namespace MCLauncher.App.ViewModels;

/// <summary>
/// 下载队列的界面行集合：按文件名复用行，避免每次进度刷新都重建整个列表。
/// 单文件下载走批量下载后，这里同样会有一行。
/// </summary>
public sealed class DownloadFileRows
{
    private readonly Dictionary<string, InstallFileItem> _rows = new(StringComparer.Ordinal);

    public ObservableCollection<InstallFileItem> Items { get; } = new();

    public void Update(IReadOnlyList<DownloadFileProgress> files)
    {
        if (files.Count == 0)
        {
            Clear();
            return;
        }

        var alive = new HashSet<string>(StringComparer.Ordinal);
        foreach (var file in files)
        {
            alive.Add(file.Name);

            if (!_rows.TryGetValue(file.Name, out var row))
            {
                row = new InstallFileItem(file.Name);
                _rows[file.Name] = row;
                Items.Add(row);
            }

            row.Update(file.BytesReceived, file.TotalBytes);
        }

        foreach (var stale in _rows.Keys.Where(key => !alive.Contains(key)).ToList())
        {
            Items.Remove(_rows[stale]);
            _rows.Remove(stale);
        }
    }

    public void Clear()
    {
        _rows.Clear();
        Items.Clear();
    }
}
