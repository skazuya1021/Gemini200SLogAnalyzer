using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Gemini200SLogAnalyzer.Models;

public sealed class ColumnSelectionItem : INotifyPropertyChanged
{
    private bool _isSelected;
    private YAxisSide _yAxisSide = YAxisSide.Left;

    public required string Name { get; init; }

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

    public YAxisSide YAxisSide
    {
        get => _yAxisSide;
        set
        {
            if (_yAxisSide == value)
            {
                return;
            }

            _yAxisSide = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(YAxisSideIndex));
        }
    }

    public int YAxisSideIndex
    {
        get => _yAxisSide == YAxisSide.Right ? 1 : 0;
        set => YAxisSide = value == 1 ? YAxisSide.Right : YAxisSide.Left;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
