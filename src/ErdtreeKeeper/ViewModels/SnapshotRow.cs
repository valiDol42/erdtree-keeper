using ErdtreeKeeper.Core;

namespace ErdtreeKeeper.ViewModels;

/// <summary>
/// Строка списка снимков вместе с признаком выбора.
///
/// Выбор хранится здесь, а не в списке. Так он переживает обновление списка,
/// его видно в модели без обращения к элементам управления, и не нужно
/// смешивать SelectedItems с Selection - в Avalonia эти два способа работают
/// с разными моделями, и совместное использование тихо ломает подсветку.
/// </summary>
public sealed class SnapshotRow(Snapshot snapshot) : ViewModelBase
{
    public Snapshot Snapshot { get; } = snapshot;

    public string Name => Snapshot.Name;
    public string Path => Snapshot.Path;
    public string SizeText => Snapshot.SizeText;
    public DateTime Created => Snapshot.Created;

    private bool _isSelected;
    public bool IsSelected
    {
        get => _isSelected;
        set => Set(ref _isSelected, value);
    }
}
