using System.Collections.ObjectModel;
using System.ComponentModel;
using OSCQueryExplorer.Core.Models;
using OSCQueryExplorer.Core.Settings;

namespace OSCQueryExplorer.ViewModels;

public sealed class PinnedItemViewModel : ObservableObject, IDisposable
{
    private TypeDisplayFormat _typeDisplay;
    public OscNode Node { get; }
    public ObservableCollection<ArgumentEditorViewModel> Editors { get; }
    public string FullPath => Node.FullPath;
    public string Name => Node.Name;
    public string TypeLabel => string.IsNullOrWhiteSpace(Node.TypeTag) ? "型情報なし" : OscTypeFormatter.Format(Node.TypeTag, _typeDisplay);
    public string ValueText => Node.Observed is { Values.Count: > 0 } observed
        ? string.Join("  ·  ", observed.Values.Select(value => value.ToDisplayString()))
        : "値はまだ受信されていません";
    public bool CanControl => Node.Access is null || Node.Access.Value.HasFlag(OscAccess.Write);

    public PinnedItemViewModel(OscNode node, ObservableCollection<ArgumentEditorViewModel> editors, TypeDisplayFormat typeDisplay)
    {
        Node = node;
        Editors = editors;
        _typeDisplay = typeDisplay;
        Node.PropertyChanged += Node_PropertyChanged;
    }

    private void Node_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(OscNode.Access)) Raise(nameof(CanControl));
        if (e.PropertyName == nameof(OscNode.TypeTag)) Raise(nameof(TypeLabel));
        if (e.PropertyName == nameof(OscNode.Observed))
        {
            Raise(nameof(ValueText));
            var values = Node.Observed?.Values;
            if (values is null) return;
            for (var i = 0; i < Math.Min(values.Count, Editors.Count); i++) Editors[i].ApplyObservedValue(values[i]);
        }
    }

    public void SetTypeDisplay(TypeDisplayFormat typeDisplay)
    {
        if (_typeDisplay == typeDisplay) return;
        _typeDisplay = typeDisplay;
        foreach (var editor in Editors) editor.DisplayFormat = typeDisplay;
        Raise(nameof(TypeLabel));
    }

    public void Dispose() => Node.PropertyChanged -= Node_PropertyChanged;
}
