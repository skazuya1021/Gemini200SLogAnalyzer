using System.Collections.ObjectModel;
using System.Data;
using System.Globalization;
using System.Text;
using System.Windows.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using Gemini200SLogAnalyzer.Models;
using Gemini200SLogAnalyzer.Services;
using Microsoft.Win32;
using ScottPlot;
using ScottPlot.Plottables;

namespace Gemini200SLogAnalyzer;

public partial class MainWindow : Window
{
    private sealed class ChartCursorContext
    {
        public required IReadOnlyList<AnalysisRow> Rows { get; init; }
        public required IReadOnlyList<string> SeriesNames { get; init; }
        public required bool UseDateTimeX { get; init; }
        public required string XAxisName { get; init; }
    }

    private readonly SettingsService _settingsService = new();
    private readonly LogMergeService _mergeService = new();
    private readonly ManualLogMergeService _manualLogMergeService = new();
    private readonly DataAnalysisService _analysisService = new();

    private MergedLogData? _mergedData;
    private LogDataSourceType _dataSourceType = LogDataSourceType.WaferLog;
    private List<string> _selectedFilePaths = [];
    private IReadOnlyList<AnalysisRow> _analysisRows = Array.Empty<AnalysisRow>();
    private List<string> _displayColumnNames = [];

    private readonly ObservableCollection<ColumnSelectionItem> _columnItems = [];
    private readonly ObservableCollection<ColumnSelectionItem> _chartColumnItems = [];
    private readonly ObservableCollection<ColumnSelectionItem> _chartLotItems = [];
    private readonly ObservableCollection<ColumnSelectionItem> _chartRecipeItems = [];
    private readonly ObservableCollection<ColumnSelectionItem> _chartCassetteItems = [];
    private readonly ObservableCollection<ScatterPairItem> _scatterPairItems = [];

    private bool _isUpdatingDateFilters;
    private bool _isUpdatingChartFilters;
    private bool _isUiReady;

    private ChartCursorContext? _chartCursorContext;
    private Crosshair? _chartCrosshair;
    private Marker? _diffMarker1;
    private Marker? _diffMarker2;
    private int? _diffPoint1Index;
    private int? _diffPoint2Index;
    private int _lastCursorRowIndex = -1;
    private IReadOnlyList<VariationAnalysisRow> _variationRows = Array.Empty<VariationAnalysisRow>();
    private string _variationSeriesName = string.Empty;
    private IReadOnlyList<AnalysisRow> _variationPlotSourceRows = Array.Empty<AnalysisRow>();
    private bool _variationPlotHasLimits;
    private int _variationPlotVisibleCount;
    private int _variationPlotTotalInView;

    private IReadOnlyList<AnalysisRow> _chartPlotSourceRows = Array.Empty<AnalysisRow>();
    private bool _chartPlotHasLimits;
    private int _chartPlotLodVisibleCount;
    private int _chartPlotLodTotalInView;

    public MainWindow()
    {
        InitializeComponent();
        ColumnListBox.ItemsSource = _columnItems;
        ChartColumnListBox.ItemsSource = _chartColumnItems;
        ChartLotListBox.ItemsSource = _chartLotItems;
        ChartRecipeListBox.ItemsSource = _chartRecipeItems;
        ChartCassetteListBox.ItemsSource = _chartCassetteItems;
        ScatterPairListBox.ItemsSource = _scatterPairItems;
        Title = $"Gemini200S Log Analyzer v{AppVersion.Current}";
        UpdateProgress(0, "待機中");
        VariationTimeModeRadioButton.Checked += VariationModeRadioButton_Checked;
        VariationValueModeRadioButton.Checked += VariationModeRadioButton_Checked;
        _isUiReady = true;
        UpdateDataSourceUi();
    }

    private LogDataSourceType GetSelectedDataSourceType()
    {
        if (DataSourceComboBox.SelectedItem is ComboBoxItem item &&
            item.Tag is string tag &&
            tag.Equals("ManualLog", StringComparison.OrdinalIgnoreCase))
        {
            return LogDataSourceType.ManualLog;
        }

        return LogDataSourceType.WaferLog;
    }

    private void DataSourceComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_isUiReady)
        {
            return;
        }

        _dataSourceType = GetSelectedDataSourceType();
        _selectedFilePaths.Clear();
        ClearPreviousSessionData();
        RefreshSelectedFilesList();
        UpdateDataSourceUi();
    }

    private void UpdateDataSourceUi()
    {
        _dataSourceType = GetSelectedDataSourceType();
        var isManualLog = _dataSourceType == LogDataSourceType.ManualLog;

        SubtitleTextBlock.Text = isManualLog
            ? "ManualLog（.csv）の合体・分析・グラフ表示"
            : "Gemini200S ログファイルの合体・分析・グラフ表示";
        MergeButton.Content = isManualLog ? "ManualLogを合体" : "ログファイルを合体";
        MergeSummaryTextBlock.Text = _mergedData is null
            ? (isManualLog ? "ManualLogデータ未読込" : "データ未読込")
            : MergeSummaryTextBlock.Text;
    }

    private string GetInputFolderSetting() =>
        _dataSourceType == LogDataSourceType.ManualLog
            ? _settingsService.Current.LastManualLogInputFolder
            : _settingsService.Current.LastInputFolder;

    private void SaveInputFolderSetting(string folder)
    {
        if (_dataSourceType == LogDataSourceType.ManualLog)
        {
            _settingsService.UpdateManualLogInputFolder(folder);
        }
        else
        {
            _settingsService.UpdateInputFolder(folder);
        }
    }

    private static DateTime ExtractSortDate(string filePath, LogDataSourceType sourceType) =>
        sourceType == LogDataSourceType.ManualLog
            ? ManualLogFileParser.ExtractSortDate(filePath)
            : LogFileParser.ExtractSortDate(filePath);

    private void ClearPreviousSessionData()
    {
        _mergedData = null;
        _analysisRows = Array.Empty<AnalysisRow>();
        _displayColumnNames = [];

        _columnItems.Clear();
        _chartColumnItems.Clear();
        _chartLotItems.Clear();
        _chartRecipeItems.Clear();
        _chartCassetteItems.Clear();
        _scatterPairItems.Clear();

        _chartCursorContext = null;
        _chartCrosshair = null;
        _diffMarker1 = null;
        _diffMarker2 = null;
        _diffPoint1Index = null;
        _diffPoint2Index = null;
        _lastCursorRowIndex = -1;

        _isUpdatingDateFilters = true;
        DateFromPicker.SelectedDate = null;
        DateToPicker.SelectedDate = null;
        _isUpdatingDateFilters = false;

        LotIdFilterComboBox.ItemsSource = new[] { "(すべて)" };
        LotIdFilterComboBox.SelectedIndex = 0;
        RecipeIdFilterComboBox.ItemsSource = new[] { "(すべて)" };
        RecipeIdFilterComboBox.SelectedIndex = 0;

        AnalysisDataGrid.Columns.Clear();
        AnalysisDataGrid.ItemsSource = null;

        ScatterXAxisComboBox.ItemsSource = null;
        ScatterYAxisComboBox.ItemsSource = null;

        _isUpdatingChartFilters = true;
        DiffMeasureCheckBox.IsChecked = false;
        _isUpdatingChartFilters = false;

        ChartDiffInfoTextBlock.Text = string.Empty;
        ResetChartCursorInfoText();

        _variationRows = Array.Empty<VariationAnalysisRow>();
        _variationSeriesName = string.Empty;
        _variationPlotSourceRows = Array.Empty<AnalysisRow>();
        _variationPlotHasLimits = false;
        _chartPlotSourceRows = Array.Empty<AnalysisRow>();
        _chartPlotHasLimits = false;
        ChartPlotStatusTextBlock.Text = string.Empty;
        VariationPlotStatusTextBlock.Text = string.Empty;
        VariationAnalysisDataGrid.ItemsSource = null;
        VariationAnalysisDataGrid.Columns.Clear();
        VariationSeriesComboBox.ItemsSource = null;
        RefreshVariationPointStatus();

        if (_isUiReady && VariationPlot?.Plot is not null)
        {
            VariationPlot.Plot.Clear();
            VariationPlot.Refresh();
        }

        if (_isUiReady)
        {
            UpdateChart();
        }

        UpdateProgress(0, "待機中");
        UpdateDataSourceUi();
    }

    private void SelectFiles_Click(object sender, RoutedEventArgs e)
    {
        ClearPreviousSessionData();
        _selectedFilePaths.Clear();
        RefreshSelectedFilesList();

        var isManualLog = _dataSourceType == LogDataSourceType.ManualLog;
        var dialog = new OpenFileDialog
        {
            Title = isManualLog ? "ManualLogファイルを選択" : "ログファイルを選択",
            Filter = isManualLog
                ? "ManualLogファイル (*.csv)|*.csv|すべてのファイル (*.*)|*.*"
                : "ログファイル (*.log)|*.log|すべてのファイル (*.*)|*.*",
            Multiselect = true,
            InitialDirectory = _settingsService.GetInitialFolder(GetInputFolderSetting())
        };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        _selectedFilePaths = dialog.FileNames
            .Where(path => !isManualLog || ManualLogFileParser.IsManualLogFile(path))
            .OrderBy(path => ExtractSortDate(path, _dataSourceType))
            .ToList();

        if (isManualLog && _selectedFilePaths.Count == 0)
        {
            MessageBox.Show("ManualLog形式のCSVファイルを選択してください。", "確認",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (_selectedFilePaths.Count > 0)
        {
            var folder = Path.GetDirectoryName(_selectedFilePaths[0]);
            if (!string.IsNullOrEmpty(folder))
            {
                SaveInputFolderSetting(folder);
            }
        }

        RefreshSelectedFilesList();
    }

    private void SelectFolder_Click(object sender, RoutedEventArgs e)
    {
        ClearPreviousSessionData();
        _selectedFilePaths.Clear();
        RefreshSelectedFilesList();

        var isManualLog = _dataSourceType == LogDataSourceType.ManualLog;
        var dialog = new OpenFolderDialog
        {
            Title = isManualLog
                ? "ManualLogファイルが含まれるフォルダを選択"
                : "ログファイルが含まれるフォルダを選択",
            InitialDirectory = _settingsService.GetInitialFolder(GetInputFolderSetting()),
            Multiselect = false
        };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        var selectedPath = dialog.FolderName;
        SaveInputFolderSetting(selectedPath);

        if (isManualLog)
        {
            _selectedFilePaths = Directory
                .GetFiles(selectedPath, "*.csv", SearchOption.AllDirectories)
                .Where(ManualLogFileParser.IsManualLogFile)
                .OrderBy(path => ExtractSortDate(path, _dataSourceType))
                .ToList();
        }
        else
        {
            _selectedFilePaths = Directory
                .GetFiles(selectedPath, "*.log", SearchOption.TopDirectoryOnly)
                .OrderBy(path => ExtractSortDate(path, _dataSourceType))
                .ToList();
        }

        RefreshSelectedFilesList();
    }

    private void RefreshSelectedFilesList()
    {
        SelectedFilesListBox.ItemsSource = null;
        SelectedFilesListBox.ItemsSource = _selectedFilePaths.Select(Path.GetFileName).ToList();
        var label = _dataSourceType == LogDataSourceType.ManualLog ? "ManualLogファイル" : "ログファイル";
        MergeSummaryTextBlock.Text = _selectedFilePaths.Count == 0
            ? $"{label}が選択されていません。"
            : $"{_selectedFilePaths.Count} 件の{label}が選択されています。";
    }

    private async void MergeLogs_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedFilePaths.Count == 0)
        {
            var message = _dataSourceType == LogDataSourceType.ManualLog
                ? "ManualLogファイルを選択してください。"
                : "ログファイルを選択してください。";
            MessageBox.Show(message, "確認", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        ClearPreviousSessionData();

        SetUiEnabled(false);
        try
        {
            var progress = new Progress<(int current, int total, string message)>(p =>
            {
                var percent = p.total > 0 ? (double)p.current / p.total * 100 : 0;
                UpdateProgress(percent, p.message);
            });

            _mergedData = _dataSourceType == LogDataSourceType.ManualLog
                ? await _manualLogMergeService.MergeAsync(_selectedFilePaths, progress)
                : await _mergeService.MergeAsync(_selectedFilePaths, progress);

            var sourceLabel = _dataSourceType == LogDataSourceType.ManualLog ? "ManualLog" : "ログ";
            MergeSummaryTextBlock.Text =
                $"{sourceLabel}合体完了: {_mergedData.Rows.Count:N0} 行, {_selectedFilePaths.Count} ファイル, {_mergedData.DataColumnNames.Length} データ列";
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

    private void EnsureValidDateRange()
    {
        if (_mergedData == null || _mergedData.Rows.Count == 0)
        {
            return;
        }

        var needsRefresh = DateFromPicker.SelectedDate is null
            || DateToPicker.SelectedDate is null
            || DateFromPicker.SelectedDate.Value.Year < 2000
            || DateToPicker.SelectedDate.Value.Year < 2000;

        if (!needsRefresh)
        {
            return;
        }

        var dateRange = DataAnalysisService.GetDateRange(_mergedData);
        if (!dateRange.HasValue)
        {
            return;
        }

        _isUpdatingDateFilters = true;
        DateFromPicker.SelectedDate = dateRange.Value.Min.Date;
        DateToPicker.SelectedDate = dateRange.Value.Max.Date;
        _isUpdatingDateFilters = false;
        UpdateFilterComboBoxesByDateRange();
    }

    private void PopulateFilters()
    {
        if (_mergedData == null)
        {
            return;
        }

        if (_mergedData.Rows.Count > 0)
        {
            var dateRange = DataAnalysisService.GetDateRange(_mergedData);
            if (dateRange.HasValue)
            {
                _isUpdatingDateFilters = true;
                DateFromPicker.SelectedDate = dateRange.Value.Min.Date;
                DateToPicker.SelectedDate = dateRange.Value.Max.Date;
                _isUpdatingDateFilters = false;
            }
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

        EnsureValidDateRange();

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
        PopulateScatterAxisOptions();
        PopulateVariationSeriesOptions();
        UpdateChartPanelVisibility();
        UpdateChart();
        MainTabControl.SelectedIndex = 1;
    }

    private void PopulateVariationSeriesOptions()
    {
        var seriesNames = _chartColumnItems
            .Where(c => c.IsSelected)
            .Select(c => c.Name)
            .DefaultIfEmpty(_displayColumnNames.FirstOrDefault() ?? string.Empty)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        VariationSeriesComboBox.ItemsSource = seriesNames;
        if (seriesNames.Count > 0)
        {
            VariationSeriesComboBox.SelectedIndex = 0;
        }

        RefreshVariationPointStatus();
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

    private void ChartColumnCheckBox_Changed(object sender, RoutedEventArgs e)
    {
        if (!_isUpdatingChartFilters && _isUiReady)
        {
            UpdateChart();
        }
    }

    private void ChartFilterCheckBox_Changed(object sender, RoutedEventArgs e)
    {
        if (!_isUpdatingChartFilters && _isUiReady)
        {
            UpdateChart();
        }
    }

    private void PopulateScatterAxisOptions()
    {
        var options = new List<string> { "DateTime" };
        options.AddRange(_displayColumnNames);

        ScatterXAxisComboBox.ItemsSource = options;
        ScatterYAxisComboBox.ItemsSource = options;

        if (options.Count > 0)
        {
            ScatterXAxisComboBox.SelectedIndex = 0;
        }

        if (options.Count > 1)
        {
            ScatterYAxisComboBox.SelectedIndex = 1;
        }

        _scatterPairItems.Clear();
        if (_displayColumnNames.Count > 0)
        {
            _scatterPairItems.Add(new ScatterPairItem
            {
                XName = "DateTime",
                YName = _displayColumnNames[0],
                IsSelected = true
            });
        }
    }

    private void ChartStyleRadioButton_Checked(object sender, RoutedEventArgs e)
    {
        if (!_isUpdatingChartFilters && _isUiReady)
        {
            UpdateChartPanelVisibility();
            UpdateChart();
        }
    }

    private void UpdateChartPanelVisibility()
    {
        if (!_isUiReady)
        {
            return;
        }

        var isScatter = GetSelectedChartStyle() == ChartStyle.Scatter;
        ScatterSettingsPanel.Visibility = isScatter ? Visibility.Visible : Visibility.Collapsed;
        TimeSeriesColumnPanel.Visibility = isScatter ? Visibility.Collapsed : Visibility.Visible;
    }

    private void ScatterAxisComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_isUpdatingChartFilters && _isUiReady && GetSelectedChartStyle() == ChartStyle.Scatter)
        {
            UpdateChart();
        }
    }

    private void AddScatterPair_Click(object sender, RoutedEventArgs e)
    {
        AddScatterPair(
            ScatterXAxisComboBox.SelectedItem as string,
            ScatterYAxisComboBox.SelectedItem as string);
    }

    private void AddScatterPairsFromSelectedY_Click(object sender, RoutedEventArgs e)
    {
        var xName = ScatterXAxisComboBox.SelectedItem as string;
        if (string.IsNullOrWhiteSpace(xName))
        {
            return;
        }

        var yNames = _chartColumnItems.Where(c => c.IsSelected).Select(c => c.Name).ToList();
        if (yNames.Count == 0)
        {
            yNames = _displayColumnNames.ToList();
        }

        foreach (var yName in yNames)
        {
            AddScatterPair(xName, yName, refresh: false);
        }

        UpdateChart();
    }

    private void AddScatterPair(string? xName, string? yName, bool refresh = true)
    {
        if (string.IsNullOrWhiteSpace(xName) || string.IsNullOrWhiteSpace(yName))
        {
            return;
        }

        if (xName.Equals(yName, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        if (_scatterPairItems.Any(p =>
                p.XName.Equals(xName, StringComparison.OrdinalIgnoreCase) &&
                p.YName.Equals(yName, StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        _scatterPairItems.Add(new ScatterPairItem
        {
            XName = xName,
            YName = yName,
            IsSelected = true
        });

        if (refresh && _isUiReady)
        {
            UpdateChart();
        }
    }

    private void ClearScatterPairs_Click(object sender, RoutedEventArgs e)
    {
        _scatterPairItems.Clear();
        UpdateChart();
    }

    private void ScatterPairCheckBox_Changed(object sender, RoutedEventArgs e)
    {
        if (!_isUpdatingChartFilters && _isUiReady)
        {
            UpdateChart();
        }
    }

    private void ChartLotSelectAll_Click(object sender, RoutedEventArgs e) =>
        SetChartItemSelection(_chartLotItems, true);

    private void ChartLotDeselectAll_Click(object sender, RoutedEventArgs e) =>
        SetChartItemSelection(_chartLotItems, false);

    private void ChartRecipeSelectAll_Click(object sender, RoutedEventArgs e) =>
        SetChartItemSelection(_chartRecipeItems, true);

    private void ChartRecipeDeselectAll_Click(object sender, RoutedEventArgs e) =>
        SetChartItemSelection(_chartRecipeItems, false);

    private void ResetChartDisplay_Click(object sender, RoutedEventArgs e)
    {
        if (_analysisRows.Count == 0)
        {
            MessageBox.Show("先にデータを表示してください。", "確認",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        _isUpdatingChartFilters = true;
        foreach (var item in _chartLotItems.Concat(_chartRecipeItems).Concat(_chartCassetteItems).Concat(_chartColumnItems))
        {
            item.IsSelected = true;
        }

        ChartLineRadioButton.IsChecked = true;
        _scatterPairItems.Clear();
        if (_displayColumnNames.Count > 0)
        {
            _scatterPairItems.Add(new ScatterPairItem
            {
                XName = "DateTime",
                YName = _displayColumnNames[0],
                IsSelected = true
            });
        }

        if (ScatterXAxisComboBox.Items.Count > 0)
        {
            ScatterXAxisComboBox.SelectedIndex = 0;
        }

        if (ScatterYAxisComboBox.Items.Count > 1)
        {
            ScatterYAxisComboBox.SelectedIndex = 1;
        }

        _isUpdatingChartFilters = false;
        ClearChartDiffPoints();
        UpdateChartPanelVisibility();
        UpdateChart();
    }

    private void SetChartItemSelection(ObservableCollection<ColumnSelectionItem> items, bool selected)
    {
        _isUpdatingChartFilters = true;
        foreach (var item in items)
        {
            item.IsSelected = selected;
        }

        _isUpdatingChartFilters = false;

        if (_isUiReady)
        {
            UpdateChart();
        }
    }

    private ChartStyle GetSelectedChartStyle()
    {
        if (ChartDotRadioButton?.IsChecked == true)
        {
            return ChartStyle.Dot;
        }

        if (ChartAreaRadioButton?.IsChecked == true)
        {
            return ChartStyle.Area;
        }

        if (ChartBarRadioButton?.IsChecked == true)
        {
            return ChartStyle.Bar;
        }

        if (ChartScatterRadioButton?.IsChecked == true)
        {
            return ChartStyle.Scatter;
        }

        return ChartStyle.Line;
    }

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

    private bool TryGetChartVisibleXRange(out double minX, out double maxX)
    {
        minX = 0;
        maxX = 0;

        if (ChartPlot?.Plot is null)
        {
            return false;
        }

        var limits = ChartPlot.Plot.Axes.GetLimits();
        minX = limits.Left;
        maxX = limits.Right;
        return minX < maxX;
    }

    private static double? GetRowAxisValue(AnalysisRow row, string axisName)
    {
        if (axisName.Equals("DateTime", StringComparison.OrdinalIgnoreCase))
        {
            return row.DateTime.ToOADate();
        }

        if (row.Values.TryGetValue(axisName, out var value) && value.HasValue)
        {
            return value.Value;
        }

        return null;
    }

    private string GetChartExportXAxisName()
    {
        if (GetSelectedChartStyle() == ChartStyle.Scatter)
        {
            var selectedPairs = _scatterPairItems.Where(p => p.IsSelected).ToList();
            if (selectedPairs.Count == 0)
            {
                return "DateTime";
            }

            if (selectedPairs.Any(p => p.XName.Equals("DateTime", StringComparison.OrdinalIgnoreCase)))
            {
                return "DateTime";
            }

            return selectedPairs[0].XName;
        }

        return "DateTime";
    }

    private IReadOnlyList<string> GetChartExportColumnNames()
    {
        if (GetSelectedChartStyle() == ChartStyle.Scatter)
        {
            var columns = new List<string>();
            foreach (var pair in _scatterPairItems.Where(p => p.IsSelected))
            {
                if (!pair.XName.Equals("DateTime", StringComparison.OrdinalIgnoreCase))
                {
                    columns.Add(pair.XName);
                }

                columns.Add(pair.YName);
            }

            return columns
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        return _chartColumnItems
            .Where(c => c.IsSelected)
            .Select(c => c.Name)
            .ToList();
    }

    private IReadOnlyList<AnalysisRow> GetChartExportRows()
    {
        var filteredRows = GetChartFilteredRows().ToList();
        if (filteredRows.Count == 0)
        {
            return filteredRows;
        }

        if (!TryGetChartVisibleXRange(out var minX, out var maxX))
        {
            return filteredRows;
        }

        var xAxisName = GetChartExportXAxisName();
        return filteredRows
            .Where(row =>
            {
                var x = GetRowAxisValue(row, xAxisName);
                return x.HasValue && x.Value >= minX && x.Value <= maxX;
            })
            .OrderBy(row => row.DateTime)
            .ToList();
    }

    private void SaveChartCsv_Click(object sender, RoutedEventArgs e)
    {
        if (_analysisRows.Count == 0)
        {
            MessageBox.Show("先にデータを表示してください。", "確認",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var columnNames = GetChartExportColumnNames();
        if (columnNames.Count == 0)
        {
            MessageBox.Show("グラフ表示項目を1つ以上選択してください。", "確認",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var exportRows = GetChartExportRows();
        if (exportRows.Count == 0)
        {
            MessageBox.Show("現在のグラフ表示範囲に該当するデータがありません。", "確認",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var dialog = new SaveFileDialog
        {
            Title = "グラフ表示範囲をCSVとして保存",
            Filter = "CSVファイル (*.csv)|*.csv",
            FileName = "ChartData.csv",
            InitialDirectory = _settingsService.GetInitialFolder(_settingsService.Current.LastOutputFolder),
            DefaultExt = ".csv"
        };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        try
        {
            CsvExportService.ExportAnalysis(exportRows, columnNames, dialog.FileName);
            MessageBox.Show(
                $"CSVファイルを保存しました。\n行数: {exportRows.Count:N0}\n項目数: {columnNames.Count}",
                "完了",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"CSV保存中にエラーが発生しました:\n{ex.Message}", "エラー",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void UpdateChart(bool resetLimits = true)
    {
        if (!_isUiReady || ChartPlot?.Plot is null)
        {
            return;
        }

        if (_analysisRows.Count == 0)
        {
            _chartPlotSourceRows = Array.Empty<AnalysisRow>();
            _chartPlotHasLimits = false;
            ChartPlotStatusTextBlock.Text = string.Empty;
            ChartPlot.Plot.Clear();
            _chartCrosshair = null;
            _diffMarker1 = null;
            _diffMarker2 = null;
            _chartCursorContext = null;
            _lastCursorRowIndex = -1;
            ResetChartCursorInfoText();
            ChartPlot.Refresh();
            return;
        }

        var filteredRows = GetChartFilteredRows().OrderBy(r => r.DateTime).ToList();
        _chartPlotSourceRows = filteredRows;

        if (filteredRows.Count == 0)
        {
            _chartPlotHasLimits = false;
            ChartPlotStatusTextBlock.Text = string.Empty;
            ChartPlot.Plot.Clear();
            _chartCrosshair = null;
            _diffMarker1 = null;
            _diffMarker2 = null;
            _chartCursorContext = null;
            _lastCursorRowIndex = -1;
            ResetChartCursorInfoText();
            ChartPlot.Refresh();
            return;
        }

        if (resetLimits)
        {
            _lastCursorRowIndex = -1;
        }

        ScottPlot.AxisLimits? savedLimits = null;
        if (!resetLimits && _chartPlotHasLimits)
        {
            savedLimits = ChartPlot.Plot.Axes.GetLimits();
        }

        var chartStyle = GetSelectedChartStyle();
        ChartPlot.Plot.Clear();
        _chartCrosshair = null;
        _diffMarker1 = null;
        _diffMarker2 = null;

        if (chartStyle == ChartStyle.Scatter)
        {
            RenderScatterChart(filteredRows, resetLimits, savedLimits);
            return;
        }

        var selectedSeries = _chartColumnItems.Where(c => c.IsSelected).Select(c => c.Name).ToList();
        if (selectedSeries.Count == 0)
        {
            _chartPlotHasLimits = false;
            ChartPlotStatusTextBlock.Text = string.Empty;
            _chartCursorContext = null;
            ResetChartCursorInfoText();
            ChartPlot.Refresh();
            return;
        }

        double xMin;
        double xMax;
        if (resetLimits || !_chartPlotHasLimits || !savedLimits.HasValue)
        {
            (xMin, xMax) = VariationPlotLodService.GetDateTimeRange(filteredRows);
        }
        else
        {
            xMin = savedLimits.Value.Left;
            xMax = savedLimits.Value.Right;
        }

        var primarySeries = selectedSeries[0];
        _chartPlotLodTotalInView = VariationPlotLodService.CountPointsInTimeRange(
            filteredRows, primarySeries, xMin, xMax);
        _chartPlotLodVisibleCount = 0;

        var seriesIndex = 0;
        var barWidthDays = CalculateBarWidthDays(
            Math.Min(_chartPlotLodTotalInView, VariationPlotLodService.DefaultMaxPoints));

        foreach (var seriesName in selectedSeries)
        {
            var plotPoints = VariationPlotLodService.GetPlotPoints(
                filteredRows, seriesName, xMin, xMax);

            if (plotPoints.Count == 0)
            {
                continue;
            }

            _chartPlotLodVisibleCount = Math.Max(_chartPlotLodVisibleCount, plotPoints.Count);

            var xs = plotPoints.Select(p => p.X).ToArray();
            var ys = plotPoints.Select(p => p.Y).ToArray();
            var markerSize = plotPoints.Count > 800 ? 0 : chartStyle == ChartStyle.Dot ? 8 : 6;

            switch (chartStyle)
            {
                case ChartStyle.Dot:
                {
                    var scatter = ChartPlot.Plot.Add.Scatter(xs, ys);
                    scatter.LegendText = seriesName;
                    scatter.LineWidth = 0;
                    scatter.MarkerSize = markerSize > 0 ? markerSize : 8;
                    break;
                }
                case ChartStyle.Area:
                {
                    var area = ChartPlot.Plot.Add.Scatter(xs, ys);
                    area.LegendText = seriesName;
                    area.LineWidth = 2;
                    area.MarkerSize = 0;
                    area.FillY = true;
                    area.FillYColor = area.Color.WithAlpha(0.25);
                    break;
                }
                case ChartStyle.Bar:
                {
                    var offset = (seriesIndex - (selectedSeries.Count - 1) / 2.0) * barWidthDays;
                    var barXs = xs.Select(x => x + offset).ToArray();
                    var bars = ChartPlot.Plot.Add.Bars(barXs, ys);
                    bars.LegendText = seriesName;
                    break;
                }
                default:
                {
                    var line = ChartPlot.Plot.Add.Scatter(xs, ys);
                    line.LegendText = seriesName;
                    line.LineWidth = 2;
                    line.MarkerSize = markerSize;
                    break;
                }
            }

            seriesIndex++;
        }

        ChartPlot.Plot.Axes.DateTimeTicksBottom();
        if (chartStyle == ChartStyle.Bar)
        {
            ChartPlot.Plot.Axes.Margins(bottom: 0);
        }

        ChartPlot.Plot.ShowLegend();
        ChartPlot.Plot.Title("Log Analysis Chart");
        ChartPlot.Plot.XLabel("DateTime");
        ChartPlot.Plot.YLabel("Value");

        _chartCursorContext = new ChartCursorContext
        {
            Rows = filteredRows,
            SeriesNames = selectedSeries,
            UseDateTimeX = true,
            XAxisName = "DateTime"
        };

        ApplyChartPlotLimits(resetLimits, savedLimits);
        SetupChartInteractionPlottables();
        RestoreDiffMarkers();
        UpdateChartPlotStatusText();

        if (resetLimits)
        {
            ResetChartCursorInfoText();
        }

        ChartPlot.Refresh();
    }

    private void ApplyChartPlotLimits(bool resetLimits, ScottPlot.AxisLimits? savedLimits)
    {
        if (resetLimits || !_chartPlotHasLimits)
        {
            ChartPlot.Plot.Axes.AutoScale();
            _chartPlotHasLimits = true;
        }
        else if (savedLimits.HasValue)
        {
            ChartPlot.Plot.Axes.SetLimits(savedLimits.Value);
        }
    }

    private void UpdateChartPlotStatusText()
    {
        if (_chartPlotSourceRows.Count == 0)
        {
            ChartPlotStatusTextBlock.Text = string.Empty;
            return;
        }

        if (_chartPlotLodVisibleCount >= _chartPlotLodTotalInView)
        {
            ChartPlotStatusTextBlock.Text =
                $"表示: {_chartPlotLodVisibleCount:N0} 点（全データ表示）";
            return;
        }

        ChartPlotStatusTextBlock.Text =
            $"表示: {_chartPlotLodVisibleCount:N0} / {_chartPlotLodTotalInView:N0} 点（ズームで詳細表示）";
    }

    private void ChartPlot_InteractionChanged(object sender, EventArgs e)
    {
        if (!_isUiReady || _chartPlotSourceRows.Count == 0)
        {
            return;
        }

        Dispatcher.BeginInvoke(
            () => UpdateChart(resetLimits: false),
            DispatcherPriority.Background);
    }

    private void ResetChartPlotView_Click(object sender, RoutedEventArgs e) =>
        UpdateChart(resetLimits: true);

    private void RenderScatterChart(
        IReadOnlyList<AnalysisRow> filteredRows,
        bool resetLimits,
        ScottPlot.AxisLimits? savedLimits)
    {
        var pairs = _scatterPairItems.Where(p => p.IsSelected).ToList();
        if (pairs.Count == 0)
        {
            _chartPlotHasLimits = false;
            ChartPlotStatusTextBlock.Text = string.Empty;
            _chartCursorContext = null;
            ResetChartCursorInfoText();
            ChartPlot.Refresh();
            return;
        }

        var useDateTimeAxis = pairs.Any(p =>
            p.XName.Equals("DateTime", StringComparison.OrdinalIgnoreCase));
        var primaryX = useDateTimeAxis ? "DateTime" : pairs[0].XName;

        double xMin;
        double xMax;
        if (resetLimits || !_chartPlotHasLimits || !savedLimits.HasValue)
        {
            (xMin, xMax) = useDateTimeAxis
                ? VariationPlotLodService.GetDateTimeRange(filteredRows)
                : VariationPlotLodService.GetAxisRange(filteredRows, primaryX);
        }
        else
        {
            xMin = savedLimits.Value.Left;
            xMax = savedLimits.Value.Right;
        }

        var seriesNames = new List<string>();
        _chartPlotLodVisibleCount = 0;
        _chartPlotLodTotalInView = 0;

        foreach (var pair in pairs)
        {
            var plotPoints = VariationPlotLodService.GetScatterPlotPoints(
                filteredRows, pair.XName, pair.YName, xMin, xMax);

            if (plotPoints.Count == 0)
            {
                continue;
            }

            _chartPlotLodVisibleCount = Math.Max(_chartPlotLodVisibleCount, plotPoints.Count);
            _chartPlotLodTotalInView = Math.Max(
                _chartPlotLodTotalInView,
                VariationPlotLodService.CountScatterPointsInRange(
                    filteredRows, pair.XName, pair.YName, xMin, xMax));

            var xs = plotPoints.Select(p => p.X).ToArray();
            var ys = plotPoints.Select(p => p.Y).ToArray();

            var scatter = ChartPlot.Plot.Add.Scatter(xs, ys);
            scatter.LegendText = pair.DisplayName;
            scatter.LineWidth = 0;
            scatter.MarkerSize = plotPoints.Count > 800 ? 4 : 8;

            if (!pair.XName.Equals("DateTime", StringComparison.OrdinalIgnoreCase))
            {
                seriesNames.Add(pair.XName);
            }

            seriesNames.Add(pair.YName);
        }

        if (useDateTimeAxis)
        {
            ChartPlot.Plot.Axes.DateTimeTicksBottom();
        }

        ChartPlot.Plot.ShowLegend();
        ChartPlot.Plot.Title("Scatter Plot");
        ChartPlot.Plot.XLabel(pairs.Count == 1 ? pairs[0].XName : "X");
        ChartPlot.Plot.YLabel(pairs.Count == 1 ? pairs[0].YName : "Y");

        _chartCursorContext = new ChartCursorContext
        {
            Rows = filteredRows,
            SeriesNames = seriesNames.Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
            UseDateTimeX = useDateTimeAxis,
            XAxisName = primaryX
        };

        ApplyChartPlotLimits(resetLimits, savedLimits);
        SetupChartInteractionPlottables();
        RestoreDiffMarkers();
        UpdateChartPlotStatusText();

        if (resetLimits)
        {
            ResetChartCursorInfoText();
        }

        ChartPlot.Refresh();
    }

    private static double? GetAxisValue(AnalysisRow row, string axisName)
    {
        if (axisName.Equals("DateTime", StringComparison.OrdinalIgnoreCase))
        {
            return row.DateTime.ToOADate();
        }

        if (row.Values.TryGetValue(axisName, out var value) && value.HasValue)
        {
            return value.Value;
        }

        return null;
    }

    private static double CalculateBarWidthDays(int rowCount)
    {
        if (rowCount <= 1)
        {
            return 0.2;
        }

        return Math.Clamp(0.8 / rowCount, 0.05, 0.25);
    }

    private void SetupChartInteractionPlottables()
    {
        if (ChartPlot?.Plot is null)
        {
            return;
        }

        _chartCrosshair = ChartPlot.Plot.Add.Crosshair(0, 0);
        _chartCrosshair.IsVisible = false;
        _chartCrosshair.LineColor = Colors.Gray.WithAlpha(0.6);
        _chartCrosshair.LinePattern = LinePattern.Dashed;
        _chartCrosshair.MarkerShape = MarkerShape.OpenCircle;
        _chartCrosshair.MarkerSize = 10;

        _diffMarker1 = ChartPlot.Plot.Add.Marker(0, 0);
        _diffMarker1.IsVisible = false;
        _diffMarker1.Color = Colors.Red;
        _diffMarker1.Size = 12;
        _diffMarker1.Shape = MarkerShape.FilledCircle;

        _diffMarker2 = ChartPlot.Plot.Add.Marker(0, 0);
        _diffMarker2.IsVisible = false;
        _diffMarker2.Color = Colors.Blue;
        _diffMarker2.Size = 12;
        _diffMarker2.Shape = MarkerShape.FilledCircle;

        RestoreDiffMarkers();
    }

    private void RestoreDiffMarkers()
    {
        if (_chartCursorContext is null || _diffMarker1 is null || _diffMarker2 is null)
        {
            return;
        }

        if (_diffPoint1Index is int index1 && index1 >= 0 && index1 < _chartCursorContext.Rows.Count)
        {
            UpdateDiffMarker(_diffMarker1, index1);
        }

        if (_diffPoint2Index is int index2 && index2 >= 0 && index2 < _chartCursorContext.Rows.Count)
        {
            UpdateDiffMarker(_diffMarker2, index2);
        }
    }

    private void UpdateDiffMarker(Marker marker, int rowIndex)
    {
        if (_chartCursorContext is null)
        {
            return;
        }

        var row = _chartCursorContext.Rows[rowIndex];
        var x = GetRowXValue(row);
        var y = GetPrimarySeriesValue(row);
        if (!x.HasValue || !y.HasValue)
        {
            marker.IsVisible = false;
            return;
        }

        marker.Location = new Coordinates(x.Value, y.Value);
        marker.IsVisible = true;
    }

    private double? GetRowXValue(AnalysisRow row)
    {
        if (_chartCursorContext is null)
        {
            return null;
        }

        if (_chartCursorContext.UseDateTimeX)
        {
            return row.DateTime.ToOADate();
        }

        return GetAxisValue(row, _chartCursorContext.XAxisName);
    }

    private double? GetPrimarySeriesValue(AnalysisRow row)
    {
        if (_chartCursorContext is null)
        {
            return null;
        }

        foreach (var seriesName in _chartCursorContext.SeriesNames)
        {
            if (row.Values.TryGetValue(seriesName, out var value) && value.HasValue)
            {
                return value.Value;
            }
        }

        return null;
    }

    private void ChartPlot_MouseMove(object sender, MouseEventArgs e)
    {
        if (!_isUiReady || _chartCursorContext is null || _chartCursorContext.Rows.Count == 0)
        {
            return;
        }

        var mouseCoords = GetChartMouseCoordinates(e);
        var rowIndex = FindNearestRowIndex(mouseCoords.X);
        if (rowIndex < 0)
        {
            HideChartCrosshair();
            return;
        }

        var row = _chartCursorContext.Rows[rowIndex];
        var x = GetRowXValue(row);
        var y = GetPrimarySeriesValue(row) ?? mouseCoords.Y;

        if (_chartCrosshair is not null && x.HasValue)
        {
            _chartCrosshair.Position = new Coordinates(x.Value, y);
            _chartCrosshair.IsVisible = true;
        }

        if (rowIndex != _lastCursorRowIndex)
        {
            _lastCursorRowIndex = rowIndex;
            ChartCursorInfoTextBlock.Text = BuildCursorInfoText(row);
        }

        ChartPlot.Refresh();
    }

    private void ChartPlot_MouseLeave(object sender, MouseEventArgs e)
    {
        HideChartCrosshair();
        _lastCursorRowIndex = -1;
        ResetChartCursorInfoText();
        ChartPlot.Refresh();
    }

    private void ChartPlot_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (!_isUiReady || DiffMeasureCheckBox.IsChecked != true || _chartCursorContext is null)
        {
            return;
        }

        var mouseCoords = GetChartMouseCoordinates(e);
        var rowIndex = FindNearestRowIndex(mouseCoords.X);
        if (rowIndex < 0)
        {
            return;
        }

        if (_diffPoint1Index is null)
        {
            _diffPoint1Index = rowIndex;
            if (_diffMarker1 is not null)
            {
                UpdateDiffMarker(_diffMarker1, rowIndex);
            }

            if (DiffAutoPoint2CheckBox.IsChecked == true)
            {
                TryApplyAutoDiffPoint2();
            }
        }
        else if (_diffPoint2Index is null)
        {
            _diffPoint2Index = rowIndex;
            if (_diffMarker2 is not null)
            {
                UpdateDiffMarker(_diffMarker2, rowIndex);
            }
        }
        else
        {
            ClearChartDiffPoints();
            _diffPoint1Index = rowIndex;
            if (_diffMarker1 is not null)
            {
                UpdateDiffMarker(_diffMarker1, rowIndex);
            }

            if (DiffAutoPoint2CheckBox.IsChecked == true)
            {
                TryApplyAutoDiffPoint2();
            }
        }

        UpdateChartDiffInfoText();
        RefreshVariationPointStatus();
        ChartPlot.Refresh();
    }

    private void ApplyAutoDiffPoint2_Click(object sender, RoutedEventArgs e)
    {
        if (_diffPoint1Index is null)
        {
            MessageBox.Show("先にグラフ上で点1を指定してください。", "確認",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (!TryApplyAutoDiffPoint2())
        {
            MessageBox.Show("点2を自動設定できませんでした。オフセット値を確認してください。", "確認",
                MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }

    private bool TryApplyAutoDiffPoint2()
    {
        if (_diffPoint1Index is not int point1Index || _chartCursorContext is null)
        {
            return false;
        }

        if (!TryParseTimeOffset(DiffOffsetValueTextBox.Text, DiffOffsetUnitComboBox, out var offset))
        {
            return false;
        }

        var target = _chartCursorContext.Rows[point1Index].DateTime.Add(offset);
        var point2Index = FindNearestRowIndexByDateTime(_chartCursorContext.Rows, target.ToOADate());
        if (point2Index < 0)
        {
            return false;
        }

        _diffPoint2Index = point2Index;
        if (_diffMarker2 is not null)
        {
            UpdateDiffMarker(_diffMarker2, point2Index);
        }

        UpdateChartDiffInfoText();
        RefreshVariationPointStatus();
        ChartPlot.Refresh();
        return true;
    }

    private static bool TryParseTimeOffset(
        string? valueText,
        ComboBox unitComboBox,
        out TimeSpan offset)
    {
        offset = TimeSpan.Zero;
        if (!double.TryParse(valueText?.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var value) &&
            !double.TryParse(valueText?.Trim(), NumberStyles.Float, CultureInfo.CurrentCulture, out value))
        {
            return false;
        }

        if (value < 0)
        {
            return false;
        }

        var unitIndex = unitComboBox.SelectedIndex;
        offset = unitIndex switch
        {
            0 => TimeSpan.FromSeconds(value),
            2 => TimeSpan.FromHours(value),
            _ => TimeSpan.FromMinutes(value)
        };

        return offset > TimeSpan.Zero;
    }

    private void DiffMeasureCheckBox_Changed(object sender, RoutedEventArgs e)
    {
        if (!_isUiReady)
        {
            return;
        }

        if (DiffMeasureCheckBox.IsChecked != true)
        {
            ClearChartDiffPoints();
            ChartPlot.Refresh();
        }
    }

    private void ClearChartDiff_Click(object sender, RoutedEventArgs e)
    {
        ClearChartDiffPoints();
        ChartPlot.Refresh();
    }

    private void ClearChartDiffPoints()
    {
        _diffPoint1Index = null;
        _diffPoint2Index = null;

        if (_diffMarker1 is not null)
        {
            _diffMarker1.IsVisible = false;
        }

        if (_diffMarker2 is not null)
        {
            _diffMarker2.IsVisible = false;
        }

        ChartDiffInfoTextBlock.Text = string.Empty;
        RefreshVariationPointStatus();
    }

    private void RefreshVariationPointStatus_Click(object sender, RoutedEventArgs e) =>
        RefreshVariationPointStatus();

    private void RefreshVariationPointStatus()
    {
        if (_chartCursorContext is null ||
            _diffPoint1Index is not int index1 ||
            index1 < 0 ||
            index1 >= _chartCursorContext.Rows.Count)
        {
            VariationPointStatusTextBlock.Text = "グラフタブで点1・点2を指定してください。";
            return;
        }

        var row1 = _chartCursorContext.Rows[index1];
        var builder = new StringBuilder();
        builder.Append("点1: ");
        builder.AppendLine(row1.DateTime.ToString("yyyy/MM/dd HH:mm:ss", CultureInfo.InvariantCulture));

        if (_diffPoint2Index is not int index2 ||
            index2 < 0 ||
            index2 >= _chartCursorContext.Rows.Count)
        {
            builder.Append("点2: （未指定）");
            VariationPointStatusTextBlock.Text = builder.ToString().TrimEnd();
            return;
        }

        var row2 = _chartCursorContext.Rows[index2];
        builder.Append("点2: ");
        builder.AppendLine(row2.DateTime.ToString("yyyy/MM/dd HH:mm:ss", CultureInfo.InvariantCulture));
        builder.Append("区間: ");
        builder.Append(FormatSignedTimeSpan(row2.DateTime - row1.DateTime));
        VariationPointStatusTextBlock.Text = builder.ToString().TrimEnd();
    }

    private void VariationModeRadioButton_Checked(object sender, RoutedEventArgs e)
    {
        if (!_isUiReady)
        {
            return;
        }

        var isTimeMode = VariationTimeModeRadioButton.IsChecked == true;
        VariationTimeIntervalPanel.Visibility = isTimeMode ? Visibility.Visible : Visibility.Collapsed;
        VariationValueIntervalPanel.Visibility = isTimeMode ? Visibility.Collapsed : Visibility.Visible;
    }

    private void RunVariationAnalysis_Click(object sender, RoutedEventArgs e)
    {
        if (_chartCursorContext is null ||
            _diffPoint1Index is not int index1 ||
            _diffPoint2Index is not int index2)
        {
            MessageBox.Show("グラフタブで点1・点2を指定してから実行してください。", "確認",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var seriesName = VariationSeriesComboBox.SelectedItem as string;
        if (string.IsNullOrWhiteSpace(seriesName))
        {
            MessageBox.Show("分析項目を選択してください。", "確認",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var rows = _chartCursorContext.Rows;
        var autoInterval = VariationAutoIntervalCheckBox.IsChecked == true;
        IReadOnlyList<VariationAnalysisRow> results;

        if (VariationTimeModeRadioButton.IsChecked == true)
        {
            var interval = autoInterval
                ? DataVariationAnalysisService.SuggestTimeInterval(rows, index1, index2)
                : ParseManualTimeInterval();

            if (interval <= TimeSpan.Zero)
            {
                MessageBox.Show("時間間隔を正しく入力してください。", "確認",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            if (autoInterval)
            {
                ApplySuggestedTimeInterval(interval);
            }

            results = DataVariationAnalysisService.AnalyzeByTimeInterval(
                rows, index1, index2, seriesName, interval);
        }
        else
        {
            var valueStep = autoInterval
                ? DataVariationAnalysisService.SuggestValueStep(rows, index1, index2, seriesName)
                : ParseManualValueStep();

            if (valueStep <= 0)
            {
                MessageBox.Show("値間隔を正しく入力してください。", "確認",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            if (autoInterval)
            {
                VariationValueStepTextBox.Text = valueStep.ToString("G6", CultureInfo.InvariantCulture);
            }

            results = DataVariationAnalysisService.AnalyzeByValueInterval(
                rows, index1, index2, seriesName, valueStep);
        }

        if (results.Count == 0)
        {
            MessageBox.Show("変動分析結果がありません。点の位置や間隔設定を確認してください。", "確認",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        _variationRows = results;
        _variationSeriesName = seriesName;
        _variationPlotSourceRows = VariationPlotLodService.GetSegmentRows(rows, index1, index2);
        BindVariationAnalysisGrid();
        UpdateVariationPlot(resetLimits: true);
        MainTabControl.SelectedIndex = 3;
    }

    private TimeSpan ParseManualTimeInterval()
    {
        if (!TryParseTimeOffset(
                VariationTimeIntervalTextBox.Text,
                VariationTimeUnitComboBox,
                out var offset))
        {
            return TimeSpan.Zero;
        }

        return offset;
    }

    private void ApplySuggestedTimeInterval(TimeSpan interval)
    {
        if (interval.TotalHours >= 1 && Math.Abs(interval.TotalHours - Math.Round(interval.TotalHours)) < 0.001)
        {
            VariationTimeUnitComboBox.SelectedIndex = 2;
            VariationTimeIntervalTextBox.Text = interval.TotalHours.ToString("0.##", CultureInfo.InvariantCulture);
            return;
        }

        if (interval.TotalMinutes >= 1 && Math.Abs(interval.TotalMinutes - Math.Round(interval.TotalMinutes)) < 0.001)
        {
            VariationTimeUnitComboBox.SelectedIndex = 1;
            VariationTimeIntervalTextBox.Text = interval.TotalMinutes.ToString("0.##", CultureInfo.InvariantCulture);
            return;
        }

        VariationTimeUnitComboBox.SelectedIndex = 0;
        VariationTimeIntervalTextBox.Text = interval.TotalSeconds.ToString("0.##", CultureInfo.InvariantCulture);
    }

    private double ParseManualValueStep()
    {
        if (double.TryParse(VariationValueStepTextBox.Text?.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
        {
            return value;
        }

        return double.TryParse(VariationValueStepTextBox.Text?.Trim(), NumberStyles.Float, CultureInfo.CurrentCulture, out value)
            ? value
            : 0;
    }

    private void BindVariationAnalysisGrid()
    {
        VariationAnalysisDataGrid.Columns.Clear();

        VariationAnalysisDataGrid.Columns.Add(new DataGridTextColumn
        {
            Header = "No",
            Binding = new Binding(nameof(VariationAnalysisRow.Sequence))
        });
        VariationAnalysisDataGrid.Columns.Add(new DataGridTextColumn
        {
            Header = "DateTime",
            Binding = new Binding(nameof(VariationAnalysisRow.DateTime))
            {
                StringFormat = "yyyy/MM/dd HH:mm:ss"
            }
        });
        VariationAnalysisDataGrid.Columns.Add(new DataGridTextColumn
        {
            Header = "Elapsed",
            Binding = new Binding(nameof(VariationAnalysisRow.ElapsedFromStart))
            {
                StringFormat = @"hh\:mm\:ss"
            }
        });
        VariationAnalysisDataGrid.Columns.Add(new DataGridTextColumn
        {
            Header = _variationSeriesName,
            Binding = new Binding(nameof(VariationAnalysisRow.Value))
            {
                StringFormat = "G6"
            }
        });
        VariationAnalysisDataGrid.Columns.Add(new DataGridTextColumn
        {
            Header = "Δ前回",
            Binding = new Binding(nameof(VariationAnalysisRow.DeltaFromPrevious))
            {
                StringFormat = "G6"
            }
        });
        VariationAnalysisDataGrid.Columns.Add(new DataGridTextColumn
        {
            Header = "Δ開始",
            Binding = new Binding(nameof(VariationAnalysisRow.DeltaFromStart))
            {
                StringFormat = "G6"
            }
        });
        VariationAnalysisDataGrid.Columns.Add(new DataGridTextColumn
        {
            Header = "前回間隔",
            Binding = new Binding(nameof(VariationAnalysisRow.IntervalFromPrevious))
            {
                StringFormat = @"hh\:mm\:ss"
            }
        });

        VariationAnalysisDataGrid.ItemsSource = _variationRows;
    }

    private void UpdateVariationPlot(bool resetLimits)
    {
        if (!_isUiReady || VariationPlot?.Plot is null || _variationPlotSourceRows.Count == 0)
        {
            return;
        }

        double xMin;
        double xMax;
        ScottPlot.AxisLimits? savedLimits = null;

        if (resetLimits || !_variationPlotHasLimits)
        {
            xMin = _variationPlotSourceRows[0].DateTime.ToOADate();
            xMax = _variationPlotSourceRows[^1].DateTime.ToOADate();
        }
        else
        {
            savedLimits = VariationPlot.Plot.Axes.GetLimits();
            xMin = savedLimits.Value.Left;
            xMax = savedLimits.Value.Right;
        }

        var plotPoints = VariationPlotLodService.GetPlotPoints(
            _variationPlotSourceRows,
            _variationSeriesName,
            xMin,
            xMax);

        _variationPlotVisibleCount = plotPoints.Count;
        _variationPlotTotalInView = VariationPlotLodService.CountPointsInTimeRange(
            _variationPlotSourceRows, _variationSeriesName, xMin, xMax);

        VariationPlot.Plot.Clear();

        if (plotPoints.Count > 0)
        {
            var xs = plotPoints.Select(p => p.X).ToArray();
            var ys = plotPoints.Select(p => p.Y).ToArray();
            var line = VariationPlot.Plot.Add.Scatter(xs, ys);
            line.LegendText = _variationSeriesName;
            line.LineWidth = 2;
            line.MarkerSize = plotPoints.Count > 800 ? 0 : 5;
        }

        if (_variationRows.Count > 0)
        {
            var sampleXs = _variationRows.Select(r => r.DateTime.ToOADate()).ToArray();
            var sampleYs = _variationRows.Select(r => r.Value).ToArray();
            var markers = VariationPlot.Plot.Add.Scatter(sampleXs, sampleYs);
            markers.LegendText = "分析サンプル";
            markers.LineWidth = 0;
            markers.MarkerSize = 8;
            markers.Color = ScottPlot.Colors.Orange;
        }

        VariationPlot.Plot.Axes.DateTimeTicksBottom();
        VariationPlot.Plot.Title("Variation Analysis Chart");
        VariationPlot.Plot.XLabel("DateTime");
        VariationPlot.Plot.YLabel(_variationSeriesName);
        VariationPlot.Plot.ShowLegend();

        if (resetLimits || !_variationPlotHasLimits)
        {
            VariationPlot.Plot.Axes.AutoScale();
            _variationPlotHasLimits = true;
        }
        else if (savedLimits.HasValue)
        {
            VariationPlot.Plot.Axes.SetLimits(savedLimits.Value);
        }

        UpdateVariationPlotStatusText();
        VariationPlot.Refresh();
    }

    private void UpdateVariationPlotStatusText()
    {
        if (_variationPlotSourceRows.Count == 0)
        {
            VariationPlotStatusTextBlock.Text = string.Empty;
            return;
        }

        if (_variationPlotVisibleCount >= _variationPlotTotalInView)
        {
            VariationPlotStatusTextBlock.Text =
                $"表示: {_variationPlotVisibleCount:N0} 点（全データ表示）";
            return;
        }

        VariationPlotStatusTextBlock.Text =
            $"表示: {_variationPlotVisibleCount:N0} / {_variationPlotTotalInView:N0} 点（ズームで詳細表示）";
    }

    private void VariationPlot_InteractionChanged(object sender, EventArgs e)
    {
        if (!_isUiReady || _variationPlotSourceRows.Count == 0)
        {
            return;
        }

        Dispatcher.BeginInvoke(
            () => UpdateVariationPlot(resetLimits: false),
            DispatcherPriority.Background);
    }

    private void ResetVariationPlotView_Click(object sender, RoutedEventArgs e)
    {
        UpdateVariationPlot(resetLimits: true);
    }

    private void ExportVariationCsv_Click(object sender, RoutedEventArgs e)
    {
        if (_variationRows.Count == 0)
        {
            MessageBox.Show("先に変動分析を実行してください。", "確認",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var dialog = new SaveFileDialog
        {
            Title = "変動分析結果をCSVとして保存",
            Filter = "CSVファイル (*.csv)|*.csv",
            FileName = "VariationAnalysis.csv",
            InitialDirectory = _settingsService.GetInitialFolder(_settingsService.Current.LastOutputFolder),
            DefaultExt = ".csv"
        };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        try
        {
            CsvExportService.ExportVariationAnalysis(_variationRows, _variationSeriesName, dialog.FileName);
            MessageBox.Show("CSVファイルを保存しました。", "完了", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"CSV保存中にエラーが発生しました:\n{ex.Message}", "エラー",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private Coordinates GetChartMouseCoordinates(MouseEventArgs e)
    {
        var position = e.GetPosition(ChartPlot);
        var mousePixel = new Pixel(position.X * ChartPlot.DisplayScale, position.Y * ChartPlot.DisplayScale);
        return ChartPlot.Plot.GetCoordinates(mousePixel);
    }

    private int FindNearestRowIndex(double mouseX)
    {
        if (_chartCursorContext is null || _chartCursorContext.Rows.Count == 0)
        {
            return -1;
        }

        if (_chartCursorContext.UseDateTimeX)
        {
            return FindNearestRowIndexByDateTime(_chartCursorContext.Rows, mouseX);
        }

        var bestIndex = -1;
        var bestDistance = double.MaxValue;

        for (var i = 0; i < _chartCursorContext.Rows.Count; i++)
        {
            var x = GetAxisValue(_chartCursorContext.Rows[i], _chartCursorContext.XAxisName);
            if (!x.HasValue)
            {
                continue;
            }

            var distance = Math.Abs(x.Value - mouseX);
            if (distance < bestDistance)
            {
                bestDistance = distance;
                bestIndex = i;
            }
        }

        return bestIndex;
    }

    private static int FindNearestRowIndexByDateTime(IReadOnlyList<AnalysisRow> rows, double mouseX)
    {
        if (rows.Count == 0)
        {
            return -1;
        }

        var targetTicks = DateTime.FromOADate(mouseX).Ticks;
        var left = 0;
        var right = rows.Count - 1;

        while (left < right)
        {
            var mid = (left + right) / 2;
            if (rows[mid].DateTime.Ticks < targetTicks)
            {
                left = mid + 1;
            }
            else
            {
                right = mid;
            }
        }

        var bestIndex = left;
        if (left > 0)
        {
            var currentDistance = Math.Abs(rows[left].DateTime.Ticks - targetTicks);
            var previousDistance = Math.Abs(rows[left - 1].DateTime.Ticks - targetTicks);
            if (previousDistance < currentDistance)
            {
                bestIndex = left - 1;
            }
        }

        return bestIndex;
    }

    private string BuildCursorInfoText(AnalysisRow row)
    {
        if (_chartCursorContext is null)
        {
            return string.Empty;
        }

        var builder = new StringBuilder();
        builder.Append("カーソル: ");
        builder.AppendLine(row.DateTime.ToString("yyyy/MM/dd HH:mm:ss", CultureInfo.InvariantCulture));

        foreach (var seriesName in _chartCursorContext.SeriesNames)
        {
            if (row.Values.TryGetValue(seriesName, out var value) && value.HasValue)
            {
                builder.Append("  ");
                builder.Append(seriesName);
                builder.Append(": ");
                builder.AppendLine(value.Value.ToString("G6", CultureInfo.InvariantCulture));
            }
        }

        return builder.ToString().TrimEnd();
    }

    private void UpdateChartDiffInfoText()
    {
        if (_chartCursorContext is null ||
            _diffPoint1Index is not int index1 ||
            index1 < 0 ||
            index1 >= _chartCursorContext.Rows.Count)
        {
            ChartDiffInfoTextBlock.Text = string.Empty;
            return;
        }

        var row1 = _chartCursorContext.Rows[index1];
        var builder = new StringBuilder();
        builder.AppendLine("【2点間差分測定】");
        builder.Append("点1: ");
        builder.AppendLine(row1.DateTime.ToString("yyyy/MM/dd HH:mm:ss", CultureInfo.InvariantCulture));

        if (_diffPoint2Index is not int index2 ||
            index2 < 0 ||
            index2 >= _chartCursorContext.Rows.Count)
        {
            builder.Append("点2: （グラフをクリックして指定）");
            ChartDiffInfoTextBlock.Text = builder.ToString().TrimEnd();
            return;
        }

        var row2 = _chartCursorContext.Rows[index2];
        builder.Append("点2: ");
        builder.AppendLine(row2.DateTime.ToString("yyyy/MM/dd HH:mm:ss", CultureInfo.InvariantCulture));

        var timeDelta = row2.DateTime - row1.DateTime;
        builder.Append("時間差 (点2 - 点1): ");
        builder.AppendLine(FormatSignedTimeSpan(timeDelta));

        foreach (var seriesName in _chartCursorContext.SeriesNames)
        {
            row1.Values.TryGetValue(seriesName, out var value1);
            row2.Values.TryGetValue(seriesName, out var value2);

            if (!value1.HasValue && !value2.HasValue)
            {
                continue;
            }

            builder.Append("  ");
            builder.Append(seriesName);
            builder.Append(": ");

            if (value1.HasValue && value2.HasValue)
            {
                var delta = value2.Value - value1.Value;
                builder.Append(value1.Value.ToString("G6", CultureInfo.InvariantCulture));
                builder.Append(" → ");
                builder.Append(value2.Value.ToString("G6", CultureInfo.InvariantCulture));
                builder.Append(" (Δ ");
                builder.Append(delta.ToString("+0.######;-0.######;0", CultureInfo.InvariantCulture));
                builder.AppendLine(")");
            }
            else if (value1.HasValue)
            {
                builder.AppendLine(value1.Value.ToString("G6", CultureInfo.InvariantCulture));
            }
            else if (value2.HasValue)
            {
                builder.AppendLine(value2.Value.ToString("G6", CultureInfo.InvariantCulture));
            }
        }

        ChartDiffInfoTextBlock.Text = builder.ToString().TrimEnd();
    }

    private static string FormatSignedTimeSpan(TimeSpan timeSpan)
    {
        var sign = timeSpan.Ticks < 0 ? "-" : string.Empty;
        var absolute = timeSpan.Duration();

        if (absolute.TotalDays >= 1)
        {
            return $"{sign}{(int)absolute.TotalDays}日{absolute.Hours}時間{absolute.Minutes}分{absolute.Seconds}秒";
        }

        if (absolute.TotalHours >= 1)
        {
            return $"{sign}{(int)absolute.TotalHours}時間{absolute.Minutes}分{absolute.Seconds}秒";
        }

        if (absolute.TotalMinutes >= 1)
        {
            return $"{sign}{(int)absolute.TotalMinutes}分{absolute.Seconds}秒";
        }

        return $"{sign}{absolute.TotalSeconds:F3}秒";
    }

    private void HideChartCrosshair()
    {
        if (_chartCrosshair is not null)
        {
            _chartCrosshair.IsVisible = false;
        }
    }

    private void ResetChartCursorInfoText()
    {
        ChartCursorInfoTextBlock.Text = _chartCursorContext is null
            ? "カーソルをグラフ上に移動すると、最寄りのデータ点の値を表示します。"
            : "カーソルをグラフ上に移動すると、最寄りのデータ点の値を表示します。";
    }

    private bool HasCursorInfoToExport() =>
        _lastCursorRowIndex >= 0 && _chartCursorContext is not null;

    private bool HasDiffInfoToExport() =>
        _diffPoint1Index is not null &&
        _diffPoint2Index is not null &&
        _chartCursorContext is not null;

    private string? BuildPngAnnotationText(bool includeCursor, bool includeDiff)
    {
        var builder = new StringBuilder();

        if (includeCursor && HasCursorInfoToExport())
        {
            builder.AppendLine(ChartCursorInfoTextBlock.Text.Trim());
        }

        if (includeDiff && HasDiffInfoToExport())
        {
            if (builder.Length > 0)
            {
                builder.AppendLine();
            }

            builder.Append(ChartDiffInfoTextBlock.Text.Trim());
        }

        return builder.Length == 0 ? null : builder.ToString().TrimEnd();
    }

    private void SaveChartPng_Click(object sender, RoutedEventArgs e)
    {
        if (_analysisRows.Count == 0)
        {
            MessageBox.Show("先にデータを表示してください。", "確認",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var includeCursor = PngIncludeCursorCheckBox.IsChecked == true;
        var includeDiff = PngIncludeDiffCheckBox.IsChecked == true;

        if (includeCursor && !HasCursorInfoToExport())
        {
            MessageBox.Show("カーソル位置データがありません。グラフ上にマウスを移動してから保存してください。", "確認",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (includeDiff && !HasDiffInfoToExport())
        {
            MessageBox.Show("2点間差分データがありません。「2点間差分測定」で点1・点2を指定してから保存してください。", "確認",
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
            var annotationText = BuildPngAnnotationText(includeCursor, includeDiff);
            ChartPngExportService.Save(ChartPlot.Plot, dialog.FileName, annotationText, ChartPlot);

            var message = annotationText is null
                ? "PNGファイルを保存しました。"
                : "PNGファイルを保存しました。（グラフ＋測定データ）";
            MessageBox.Show(message, "完了", MessageBoxButton.OK, MessageBoxImage.Information);
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
