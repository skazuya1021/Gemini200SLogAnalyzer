using System.Collections.ObjectModel;
using System.Data;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using Gemini200SLogAnalyzer.Models;
using Gemini200SLogAnalyzer.Services;
using Microsoft.Win32;
using ScottPlot;

namespace Gemini200SLogAnalyzer;

public partial class MainWindow : Window
{
    private readonly SettingsService _settingsService = new();
    private readonly LogMergeService _mergeService = new();
    private readonly DataAnalysisService _analysisService = new();

    private MergedLogData? _mergedData;
    private List<string> _selectedFilePaths = [];
    private IReadOnlyList<AnalysisRow> _analysisRows = Array.Empty<AnalysisRow>();
    private List<string> _displayColumnNames = [];

    private readonly ObservableCollection<ColumnSelectionItem> _columnItems = [];
    private readonly ObservableCollection<ColumnSelectionItem> _chartColumnItems = [];
    private readonly ObservableCollection<ColumnSelectionItem> _chartLotItems = [];
    private readonly ObservableCollection<ColumnSelectionItem> _chartRecipeItems = [];
    private readonly ObservableCollection<ColumnSelectionItem> _chartCassetteItems = [];

    private bool _isUpdatingDateFilters;
    private bool _isUiReady;

    public MainWindow()
    {
        InitializeComponent();
        ColumnListBox.ItemsSource = _columnItems;
        ChartColumnListBox.ItemsSource = _chartColumnItems;
        ChartLotListBox.ItemsSource = _chartLotItems;
        ChartRecipeListBox.ItemsSource = _chartRecipeItems;
        ChartCassetteListBox.ItemsSource = _chartCassetteItems;
        UpdateProgress(0, "待機中");
        _isUiReady = true;
    }

    private void SelectFiles_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "ログファイルを選択",
            Filter = "ログファイル (*.log)|*.log|すべてのファイル (*.*)|*.*",
            Multiselect = true,
            InitialDirectory = _settingsService.GetInitialFolder(_settingsService.Current.LastInputFolder)
        };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        _selectedFilePaths = dialog.FileNames.OrderBy(LogFileParser.ExtractSortDate).ToList();
        if (_selectedFilePaths.Count > 0)
        {
            var folder = Path.GetDirectoryName(_selectedFilePaths[0]);
            if (!string.IsNullOrEmpty(folder))
            {
                _settingsService.UpdateInputFolder(folder);
            }
        }

        RefreshSelectedFilesList();
    }

    private void SelectFolder_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog
        {
            Title = "ログファイルが含まれるフォルダを選択",
            InitialDirectory = _settingsService.GetInitialFolder(_settingsService.Current.LastInputFolder),
            Multiselect = false
        };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        var selectedPath = dialog.FolderName;
        _settingsService.UpdateInputFolder(selectedPath);
        _selectedFilePaths = Directory
            .GetFiles(selectedPath, "*.log", SearchOption.TopDirectoryOnly)
            .OrderBy(LogFileParser.ExtractSortDate)
            .ToList();

        RefreshSelectedFilesList();
    }

    private void RefreshSelectedFilesList()
    {
        SelectedFilesListBox.ItemsSource = null;
        SelectedFilesListBox.ItemsSource = _selectedFilePaths.Select(Path.GetFileName).ToList();
        MergeSummaryTextBlock.Text = _selectedFilePaths.Count == 0
            ? "ファイルが選択されていません。"
            : $"{_selectedFilePaths.Count} 件のログファイルが選択されています。";
    }

    private async void MergeLogs_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedFilePaths.Count == 0)
        {
            MessageBox.Show("ログファイルを選択してください。", "確認", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        SetUiEnabled(false);
        try
        {
            var progress = new Progress<(int current, int total, string message)>(p =>
            {
                var percent = p.total > 0 ? (double)p.current / p.total * 100 : 0;
                UpdateProgress(percent, p.message);
            });

            _mergedData = await _mergeService.MergeAsync(_selectedFilePaths, progress);
            MergeSummaryTextBlock.Text =
                $"合体完了: {_mergedData.Rows.Count:N0} 行, {_selectedFilePaths.Count} ファイル, {_mergedData.DataColumnNames.Length} データ列";
            UpdateProgress(100, "合体処理が完了しました。");
            PopulateColumnSelectors();
            PopulateFilters();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"合体処理中にエラーが発生しました:\n{ex.Message}", "エラー",
                MessageBoxButton.OK, MessageBoxImage.Error);
            UpdateProgress(0, "エラーが発生しました。");
        }
        finally
        {
            SetUiEnabled(true);
        }
    }

    private void ExportCsv_Click(object sender, RoutedEventArgs e)
    {
        if (_mergedData == null)
        {
            MessageBox.Show("先にログファイルを合体してください。", "確認",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var settings = _settingsService.Current;
        var dialog = new SaveFileDialog
        {
            Title = "CSVファイルとして保存",
            Filter = "CSVファイル (*.csv)|*.csv",
            FileName = settings.LastOutputFileName,
            InitialDirectory = _settingsService.GetInitialFolder(settings.LastOutputFolder),
            DefaultExt = ".csv"
        };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        try
        {
            CsvExportService.Export(_mergedData, dialog.FileName);
            var folder = Path.GetDirectoryName(dialog.FileName) ?? string.Empty;
            var fileName = Path.GetFileName(dialog.FileName);
            _settingsService.UpdateOutput(folder, fileName);
            MessageBox.Show("CSVファイルを保存しました。", "完了", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"CSV保存中にエラーが発生しました:\n{ex.Message}", "エラー",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void PopulateColumnSelectors()
    {
        _columnItems.Clear();
        _chartColumnItems.Clear();

        if (_mergedData == null)
        {
            return;
        }

        foreach (var name in _mergedData.DataColumnNames)
        {
            _columnItems.Add(new ColumnSelectionItem { Name = name, IsSelected = false });
        }
    }

    private void PopulateFilters()
    {
        if (_mergedData == null)
        {
            return;
        }

        if (_mergedData.Rows.Count > 0)
        {
            var dates = _mergedData.Rows
                .Select(r => LogFileParser.ExtractSortDate(r[0]))
                .OrderBy(d => d)
                .ToList();
            _isUpdatingDateFilters = true;
            DateFromPicker.SelectedDate = dates.First();
            DateToPicker.SelectedDate = dates.Last();
            _isUpdatingDateFilters = false;
        }

        UpdateFilterComboBoxesByDateRange();
    }

    private void DateRangePicker_SelectedDateChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isUpdatingDateFilters || _mergedData == null)
        {
            return;
        }

        UpdateFilterComboBoxesByDateRange();
    }

    private void UpdateFilterComboBoxesByDateRange()
    {
        if (_mergedData == null)
        {
            return;
        }

        var previousLot = LotIdFilterComboBox.SelectedItem as string;
        var previousRecipe = RecipeIdFilterComboBox.SelectedItem as string;

        var lotIds = _analysisService.GetDistinctValuesInDateRange(
            _mergedData, 1, DateFromPicker.SelectedDate, DateToPicker.SelectedDate);
        var recipeIds = _analysisService.GetDistinctValuesInDateRange(
            _mergedData, 3, DateFromPicker.SelectedDate, DateToPicker.SelectedDate);

        LotIdFilterComboBox.ItemsSource = new[] { "(すべて)" }.Concat(lotIds).ToList();
        RecipeIdFilterComboBox.ItemsSource = new[] { "(すべて)" }.Concat(recipeIds).ToList();

        LotIdFilterComboBox.SelectedItem = previousLot is not null &&
            (previousLot == "(すべて)" || lotIds.Contains(previousLot, StringComparer.OrdinalIgnoreCase))
            ? previousLot
            : "(すべて)";

        RecipeIdFilterComboBox.SelectedItem = previousRecipe is not null &&
            (previousRecipe == "(すべて)" || recipeIds.Contains(previousRecipe, StringComparer.OrdinalIgnoreCase))
            ? previousRecipe
            : "(すべて)";
    }

    private void ColumnListBox_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not System.Windows.Controls.ListBox listBox)
        {
            return;
        }

        var item = ItemsControl.ContainerFromElement(listBox, e.OriginalSource as DependencyObject) as ListBoxItem;
        if (item?.DataContext is not ColumnSelectionItem columnItem)
        {
            return;
        }

        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
        {
            columnItem.IsSelected = !columnItem.IsSelected;
            e.Handled = true;
            return;
        }

        columnItem.IsSelected = !columnItem.IsSelected;
        item.IsSelected = true;
        e.Handled = true;
    }

    private StatisticType GetSelectedStatistics()
    {
        var stats = StatisticType.Median;
        if (AverageCheckBox.IsChecked == true)
        {
            stats |= StatisticType.Average;
        }

        if (MaxCheckBox.IsChecked == true)
        {
            stats |= StatisticType.Max;
        }

        if (MinCheckBox.IsChecked == true)
        {
            stats |= StatisticType.Min;
        }

        return stats;
    }

    private void ShowData_Click(object sender, RoutedEventArgs e)
    {
        if (_mergedData == null)
        {
            MessageBox.Show("先にログファイルを合体してください。", "確認",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var selectedColumns = _columnItems.Where(c => c.IsSelected).Select(c => c.Name).ToList();
        if (selectedColumns.Count == 0)
        {
            MessageBox.Show("表示する項目を1つ以上選択してください。", "確認",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var stats = GetSelectedStatistics();
        var lotFilter = LotIdFilterComboBox.SelectedItem as string;
        var recipeFilter = RecipeIdFilterComboBox.SelectedItem as string;

        _analysisRows = _analysisService.Analyze(
            _mergedData,
            selectedColumns,
            stats,
            DateFromPicker.SelectedDate,
            DateToPicker.SelectedDate,
            lotFilter,
            recipeFilter);

        _displayColumnNames = BuildDisplayColumnNames(selectedColumns, stats);
        BindAnalysisGrid();

        _chartColumnItems.Clear();
        foreach (var name in _displayColumnNames)
        {
            _chartColumnItems.Add(new ColumnSelectionItem { Name = name, IsSelected = true });
        }

        PopulateChartMetadataFilters();
        UpdateChart();
        MainTabControl.SelectedIndex = 1;
    }

    private void PopulateChartMetadataFilters()
    {
        _chartLotItems.Clear();
        _chartRecipeItems.Clear();
        _chartCassetteItems.Clear();

        foreach (var lotId in _analysisRows.Select(r => r.LotId).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x))
        {
            _chartLotItems.Add(new ColumnSelectionItem { Name = lotId, IsSelected = true });
        }

        foreach (var recipeId in _analysisRows.Select(r => r.RecipeId).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x))
        {
            _chartRecipeItems.Add(new ColumnSelectionItem { Name = recipeId, IsSelected = true });
        }

        foreach (var cassette in _analysisRows.Select(r => r.Cassette).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x))
        {
            _chartCassetteItems.Add(new ColumnSelectionItem { Name = cassette, IsSelected = true });
        }
    }

    private static List<string> BuildDisplayColumnNames(IEnumerable<string> selectedColumns, StatisticType stats)
    {
        var names = new List<string>();
        foreach (var column in selectedColumns)
        {
            if (stats.HasFlag(StatisticType.Median))
            {
                names.Add(column + StatisticsHelper.GetSuffix(StatisticType.Median));
            }

            if (stats.HasFlag(StatisticType.Average))
            {
                names.Add(column + StatisticsHelper.GetSuffix(StatisticType.Average));
            }

            if (stats.HasFlag(StatisticType.Max))
            {
                names.Add(column + StatisticsHelper.GetSuffix(StatisticType.Max));
            }

            if (stats.HasFlag(StatisticType.Min))
            {
                names.Add(column + StatisticsHelper.GetSuffix(StatisticType.Min));
            }
        }

        return names;
    }

    private void BindAnalysisGrid()
    {
        var table = new DataTable();
        table.Columns.Add("DateTime", typeof(DateTime));
        table.Columns.Add("Cassette", typeof(string));
        table.Columns.Add("LotID", typeof(string));
        table.Columns.Add("RecipeID", typeof(string));

        foreach (var column in _displayColumnNames)
        {
            table.Columns.Add(column, typeof(double));
        }

        foreach (var row in _analysisRows)
        {
            var values = new object[4 + _displayColumnNames.Count];
            values[0] = row.DateTime;
            values[1] = row.Cassette;
            values[2] = row.LotId;
            values[3] = row.RecipeId;

            for (var i = 0; i < _displayColumnNames.Count; i++)
            {
                var colName = _displayColumnNames[i];
                values[4 + i] = row.Values.TryGetValue(colName, out var val) && val.HasValue
                    ? val.Value
                    : DBNull.Value;
            }

            table.Rows.Add(values);
        }

        AnalysisDataGrid.Columns.Clear();
        foreach (DataColumn dataColumn in table.Columns)
        {
            AnalysisDataGrid.Columns.Add(new DataGridTextColumn
            {
                Header = dataColumn.ColumnName,
                Binding = new Binding($"[{dataColumn.ColumnName}]")
            });
        }

        AnalysisDataGrid.ItemsSource = table.DefaultView;
    }

    private void ExportExcel_Click(object sender, RoutedEventArgs e)
    {
        if (_analysisRows.Count == 0)
        {
            MessageBox.Show("先にデータを表示してください。", "確認",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var dialog = new SaveFileDialog
        {
            Title = "Excel形式で保存",
            Filter = "Excelファイル (*.xlsx)|*.xlsx",
            FileName = "AnalysisResult.xlsx",
            InitialDirectory = _settingsService.GetInitialFolder(_settingsService.Current.LastOutputFolder),
            DefaultExt = ".xlsx"
        };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        try
        {
            ExcelExportService.ExportAnalysis(_analysisRows, _displayColumnNames, dialog.FileName);
            MessageBox.Show("Excelファイルを保存しました。", "完了", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Excel保存中にエラーが発生しました:\n{ex.Message}", "エラー",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void UpdateChart_Click(object sender, RoutedEventArgs e) => UpdateChart();

    private void ChartColumnCheckBox_Changed(object sender, RoutedEventArgs e) => UpdateChart();

    private void ChartFilterCheckBox_Changed(object sender, RoutedEventArgs e) => UpdateChart();

    private void ChartStyleRadioButton_Checked(object sender, RoutedEventArgs e)
    {
        if (_isUiReady)
        {
            UpdateChart();
        }
    }

    private ChartStyle GetSelectedChartStyle() =>
        ChartDotRadioButton?.IsChecked == true ? ChartStyle.Dot : ChartStyle.Line;

    private IEnumerable<AnalysisRow> GetChartFilteredRows()
    {
        var selectedLots = _chartLotItems.Where(x => x.IsSelected).Select(x => x.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var selectedRecipes = _chartRecipeItems.Where(x => x.IsSelected).Select(x => x.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var selectedCassettes = _chartCassetteItems.Where(x => x.IsSelected).Select(x => x.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);

        return _analysisRows.Where(row =>
            selectedLots.Contains(row.LotId) &&
            selectedRecipes.Contains(row.RecipeId) &&
            selectedCassettes.Contains(row.Cassette));
    }

    private void UpdateChart()
    {
        if (!_isUiReady || ChartPlot?.Plot is null)
        {
            return;
        }

        ChartPlot.Plot.Clear();

        if (_analysisRows.Count == 0)
        {
            ChartPlot.Refresh();
            return;
        }

        var filteredRows = GetChartFilteredRows().ToList();
        if (filteredRows.Count == 0)
        {
            ChartPlot.Refresh();
            return;
        }

        var selectedSeries = _chartColumnItems.Where(c => c.IsSelected).Select(c => c.Name).ToList();
        if (selectedSeries.Count == 0)
        {
            ChartPlot.Refresh();
            return;
        }

        var chartStyle = GetSelectedChartStyle();
        var isLineChart = chartStyle == ChartStyle.Line;

        foreach (var seriesName in selectedSeries)
        {
            var points = filteredRows
                .Select(r => new
                {
                    X = r.DateTime.ToOADate(),
                    Y = r.Values.TryGetValue(seriesName, out var v) && v.HasValue ? v.Value : double.NaN
                })
                .Where(p => !double.IsNaN(p.Y))
                .OrderBy(p => p.X)
                .ToList();

            if (points.Count == 0)
            {
                continue;
            }

            var xs = points.Select(p => p.X).ToArray();
            var ys = points.Select(p => p.Y).ToArray();

            var scatter = ChartPlot.Plot.Add.Scatter(xs, ys);
            scatter.LegendText = seriesName;
            scatter.LineWidth = isLineChart ? 2 : 0;
            scatter.MarkerSize = isLineChart ? 6 : 8;
        }

        ChartPlot.Plot.Axes.DateTimeTicksBottom();
        ChartPlot.Plot.ShowLegend();
        ChartPlot.Plot.Title("Log Analysis Chart");
        ChartPlot.Plot.XLabel("DateTime");
        ChartPlot.Plot.YLabel("Value");
        ChartPlot.Refresh();
    }

    private void SaveChartPng_Click(object sender, RoutedEventArgs e)
    {
        if (_analysisRows.Count == 0)
        {
            MessageBox.Show("先にデータを表示してください。", "確認",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var dialog = new SaveFileDialog
        {
            Title = "グラフをPNGとして保存",
            Filter = "PNG画像 (*.png)|*.png",
            FileName = "Chart.png",
            InitialDirectory = _settingsService.GetInitialFolder(_settingsService.Current.LastOutputFolder),
            DefaultExt = ".png"
        };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        try
        {
            ChartPlot.Plot.SavePng(dialog.FileName, 1200, 700);
            MessageBox.Show("PNGファイルを保存しました。", "完了", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"PNG保存中にエラーが発生しました:\n{ex.Message}", "エラー",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void UpdateProgress(double percent, string message)
    {
        ProgressBar.Value = Math.Clamp(percent, 0, 100);
        ProgressStatusTextBlock.Text = message;
    }

    private void SetUiEnabled(bool enabled)
    {
        IsEnabled = enabled;
    }
}
