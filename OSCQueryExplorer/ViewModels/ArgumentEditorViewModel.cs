using System.Globalization;
using OSCQueryExplorer.Core.Models;
using OSCQueryExplorer.Core.Settings;

namespace OSCQueryExplorer.ViewModels;

public sealed class ArgumentEditorViewModel : ObservableObject
{
    private string _text = string.Empty;
    private bool _boolean;
    private double _numericValue;
    private TypeDisplayFormat _displayFormat;
    private bool _isApplyingObservedValue;
    public bool IsDirty { get; private set; }
    public int Index { get; init; }
    public char TypeTag { get; init; }
    public string? Description { get; init; }
    public string TypeLabel => OscTypeFormatter.Format(TypeTag.ToString(), DisplayFormat);
    public TypeDisplayFormat DisplayFormat
    {
        get => _displayFormat;
        set
        {
            if (!Set(ref _displayFormat, value)) return;
            Raise(nameof(TypeLabel));
        }
    }
    public bool IsBoolean => TypeTag is 'T' or 'F';
    public bool IsNumeric => TypeTag is 'i' or 'f' or 'h' or 'd';
    public bool HasSlider { get; init; }
    public double Minimum { get; init; }
    public double Maximum { get; init; } = 1;
    public double TickFrequency => TypeTag is 'i' or 'h' ? 1 : Math.Max((Maximum - Minimum) / 100, 0.001);
    public bool CanSend => TypeTag is 'i' or 'f' or 's' or 'h' or 'd' or 'T' or 'F';
    public string Text
    {
        get => _text;
        set
        {
            if (Set(ref _text, value) && !_isApplyingObservedValue) IsDirty = true;
        }
    }
    public bool Boolean
    {
        get => _boolean;
        set
        {
            if (Set(ref _boolean, value) && !_isApplyingObservedValue) IsDirty = true;
        }
    }
    public double NumericValue
    {
        get => _numericValue;
        set
        {
            var normalized = TypeTag is 'i' or 'h' ? Math.Round(value) : value;
            if (!Set(ref _numericValue, normalized)) return;
            Text = normalized.ToString(TypeTag is 'i' or 'h' ? "0" : "0.###", CultureInfo.InvariantCulture);
        }
    }

    public void SetValue(OscValue value)
    {
        _isApplyingObservedValue = true;
        try
        {
            Boolean = value.Kind == OscValueKind.True || value.Value is true;
            Text = value.Kind == OscValueKind.String
                ? Convert.ToString(value.Value, CultureInfo.InvariantCulture) ?? string.Empty
                : value.ToDisplayString();
            if (IsNumeric && double.TryParse(Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var numeric))
                _numericValue = Math.Clamp(numeric, Minimum, Maximum);
            Raise(nameof(NumericValue));
            IsDirty = false;
        }
        finally
        {
            _isApplyingObservedValue = false;
        }
    }

    public void ApplyObservedValue(OscValue value)
    {
        if (!IsDirty) SetValue(value);
    }

    public void MarkClean() => IsDirty = false;

    public OscValue? TryGetValue() => TypeTag switch
    {
        'i' when int.TryParse(Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) => new(OscValueKind.Int32, value),
        'f' when float.TryParse(Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) => new(OscValueKind.Float32, value),
        's' => new(OscValueKind.String, Text),
        'h' when long.TryParse(Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) => new(OscValueKind.Int64, value),
        'd' when double.TryParse(Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) => new(OscValueKind.Float64, value),
        'T' or 'F' => new(Boolean ? OscValueKind.True : OscValueKind.False, Boolean),
        _ => null
    };
}
