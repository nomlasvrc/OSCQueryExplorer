using OSCQueryExplorer.Core.History;
using OSCQueryExplorer.Core.Settings;

namespace OSCQueryExplorer.ViewModels;

public sealed class LogEntryViewModel : ObservableObject
{
    private TypeDisplayFormat _typeDisplay;

    public LogEntryViewModel(HistoryEntry entry, TypeDisplayFormat typeDisplay)
    {
        Entry = entry;
        _typeDisplay = typeDisplay;
    }

    public HistoryEntry Entry { get; }
    public string DisplayText => Entry.FormatDisplayText(_typeDisplay);

    public void SetTypeDisplay(TypeDisplayFormat typeDisplay)
    {
        if (_typeDisplay == typeDisplay) return;
        _typeDisplay = typeDisplay;
        Raise(nameof(DisplayText));
    }
}
