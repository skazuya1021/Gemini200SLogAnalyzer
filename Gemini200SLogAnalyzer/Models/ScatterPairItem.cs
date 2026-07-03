using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Gemini200SLogAnalyzer.Models;

public sealed class ScatterPairItem : INotifyPropertyChanged
{
    private bool _isSelected = true;

    public required string XName { get; init; }
    public required string YName { get; init; }

    public string DisplayName => $"{YName} vs {XName}";

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (_isSelected == value)
            {
                return;
            }

            _isSelected = value;
            OnPropertyChanged();
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
