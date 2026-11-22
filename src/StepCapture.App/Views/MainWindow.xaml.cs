using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.Win32;
using StepCapture.App.Imaging;
using StepCapture.App.Recording;
using StepCapture.App.Themes;
using StepCapture.Core.Export;
using StepCapture.Core.Model;
using StepCapture.Core.Storage;
using DrawingRect = System.Drawing.Rectangle;
using WpfPoint = System.Windows.Point;

namespace StepCapture.App.Views;

public partial class MainWindow : Window
{
    private readonly ObservableCollection<StepItem> _steps = new();
    private Guide _guide = new();
    private string? _filePath;
    private bool _dirty;
    private bool _loading;

    private Recorder? _recorder;
    private RecordingBar? _bar;
    private int _recordedCount;

    private WpfPoint? _selectStart;

    public MainWindow()
    {
        InitializeComponent();
        StepList.ItemsSource = _steps;
        _steps.CollectionChanged += (_, _) => { Renumber(); UpdateViewState(); };

        foreach (var exporter in Exporters.All)
        {
            var item = new MenuItem { Header = exporter.Name + "  (" + exporter.Extension + ")", Tag = exporter };
            item.Click += OnExport;
            ExportMenu.Items.Add(item);
        }

        InputBindings.Add(new KeyBinding(new RelayCommand(() => OnNew(this, new RoutedEventArgs())), Key.N, ModifierKeys.Control));
        InputBindings.Add(new KeyBinding(new RelayCommand(() => OnOpen(this, new RoutedEventArgs())), Key.O, ModifierKeys.Control));
        InputBindings.Add(new KeyBinding(new RelayCommand(() => OnSave(this, new RoutedEventArgs())), Key.S, ModifierKeys.Control));
        Drop += OnFileDrop;

        ThemeManager.Changed += UpdateThemeToggle;
        UpdateThemeToggle();
        LoadGuide(new Guide(), null);
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        ThemeManager.ApplyTitleBar(this);
    }

    // ---------------- appearance ----------------

    private void OnToggleTheme(object sender, RoutedEventArgs e) => ThemeManager.Toggle();

    /// <summary>The pill names the mode it switches to, like "Light mode" while dark.</summary>
    private void UpdateThemeToggle()
    {
        bool dark = ThemeManager.Current == AppTheme.Dark;
        ThemeIcon.Text = dark ? "\u2600" : "\u263E"; // sun / moon
        ThemeLabel.Text = dark ? "Light mode" : "Dark mode";
    }

    // ---------------- guide state ----------------

    private StepItem? Selected => StepList.SelectedItem as StepItem;

    private void LoadGuide(Guide guide, string? path)
    {
        _loading = true;
        _guide = guide;
        _filePath = path;
        _steps.Clear();
        foreach (var s in guide.Steps) _steps.Add(new StepItem(s));
        TitleBox.Text = guide.Title;
        DescriptionBox.Text = guide.Description;
        _loading = false;
        SetDirty(false);
        if (_steps.Count > 0) StepList.SelectedIndex = 0;
        UpdateViewState();
    }

    /// <summary>Copies the editor state back into the <see cref="Guide"/> model.</summary>
    private Guide SyncGuide()
    {
        _guide.Title = string.IsNullOrWhiteSpace(TitleBox.Text) ? "Untitled guide" : TitleBox.Text.Trim();
        _guide.Description = DescriptionBox.Text;
        _guide.Steps = _steps.Select(i => i.Model).ToList();
        return _guide;
    }

    private void SetDirty(bool dirty)
    {
        _dirty = dirty;
        string name = _filePath is null ? "Unsaved guide" : Path.GetFileName(_filePath);
        Title = $"{(dirty ? "• " : "")}{name} - StepCapture";
        StatusText.Text = dirty && _steps.Count > 0 ? "Unsaved changes" : "";
        SectionTitle.Text = _steps.Count == 0 ? "STEPS" : $"STEPS  ·  {_steps.Count}";
    }

    private void Renumber()
    {
        for (int i = 0; i < _steps.Count; i++) _steps[i].Number = i + 1;
    }

    private void UpdateViewState()
    {
        bool empty = _steps.Count == 0;
        EmptyState.Visibility = empty ? Visibility.Visible : Visibility.Collapsed;
        StepList.Visibility = empty ? Visibility.Collapsed : Visibility.Visible;
        DetailPane.Visibility = Selected is null ? Visibility.Collapsed : Visibility.Visible;
        ExportButton.IsEnabled = !empty;
        if (!_loading) SetDirty(_dirty);
    }

    private void OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        DetailPane.DataContext = Selected;
        ClearSelection();
        if (Selected is { } s) StepHeader.Text = $"STEP {s.Number} OF {_steps.Count}";
        UpdateViewState();
    }

    private void OnGuideTextChanged(object sender, TextChangedEventArgs e)
    {
        if (!_loading) SetDirty(true);
    }

    private void OnStepTextChanged(object sender, TextChangedEventArgs e)
    {
        if (!_loading && ((TextBox)sender).IsKeyboardFocusWithin) SetDirty(true);
    }

    /// <summary>Asks to save unsaved changes. Returns false if the user cancelled.</summary>
    private bool ConfirmDiscard()
    {
        if (!_dirty || _steps.Count == 0) return true;
        var answer = MessageBox.Show(this, "Save changes to this guide first?", "StepCapture",
            MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
        return answer switch
        {
            MessageBoxResult.Yes => Save(saveAs: false),
            MessageBoxResult.No => true,
            _ => false,
        };
    }

    // ---------------- recording ----------------

    private void OnRecord(object sender, RoutedEventArgs e)
    {
        if (_recorder is not null) return;

        if (_steps.Count > 0)
        {
            var answer = MessageBox.Show(this,
                "Add the new steps to the end of this guide?\n\nYes: add to this guide\nNo: start a new guide",
                "Record", MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
            if (answer == MessageBoxResult.Cancel) return;
            if (answer == MessageBoxResult.No)
            {
                if (!ConfirmDiscard()) return;
                LoadGuide(new Guide(), null);
            }
        }

        _recordedCount = 0;
        _recorder = new Recorder(Dispatcher);
        _recorder.StepAdded += OnRecordedStepAdded;
        _recorder.StepUpdated += OnRecordedStepUpdated;
        _recorder.StepRemoved += OnRecordedStepRemoved;
        _recorder.PauseHotkeyPressed += TogglePause;
        _recorder.StopHotkeyPressed += StopRecording;

        _bar = new RecordingBar();
        _bar.PauseClicked += TogglePause;
        _bar.StopClicked += StopRecording;
        _bar.Show();

        WindowState = WindowState.Minimized;
        try
        {
            _recorder.Start();
        }
        catch (Exception ex)
        {
            Log.Error("Start recording", ex);
            MessageBox.Show(this, "Could not start recording: " + ex.Message, "StepCapture", MessageBoxButton.OK, MessageBoxImage.Error);
            CleanupRecording();
            WindowState = WindowState.Normal;
        }
    }

    private void TogglePause()
    {
        if (_recorder is null || _bar is null) return;
        _recorder.IsPaused = !_recorder.IsPaused;
        _bar.SetPaused(_recorder.IsPaused);
    }

    private void StopRecording()
    {
        if (_recorder is null) return;
        Mouse.OverrideCursor = Cursors.Wait;
        try { _recorder.Stop(); }
        finally { Mouse.OverrideCursor = null; }

        // Let the last StepAdded/StepUpdated callbacks queued by the recorder run before cleanup.
        Dispatcher.BeginInvoke(() =>
        {
            CleanupRecording();
            WindowState = WindowState.Normal;
            Activate();
            if (_recordedCount > 0)
            {
                int first = Math.Max(0, _steps.Count - _recordedCount);
                StepList.SelectedIndex = first;
                StepList.ScrollIntoView(StepList.SelectedItem);
            }
        }, System.Windows.Threading.DispatcherPriority.Background);
    }

    private void CleanupRecording()
    {
        _recorder?.Dispose();
        _recorder = null;
        _bar?.Close();
        _bar = null;
    }

    private void OnRecordedStepAdded(Step step)
    {
        _steps.Add(new StepItem(step));
        _recordedCount++;
        _bar?.SetStepCount(_recordedCount);
        SetDirty(true);
    }

    private void OnRecordedStepUpdated(Step step) =>
        _steps.FirstOrDefault(i => i.Model.Id == step.Id)?.Refresh();

    private void OnRecordedStepRemoved(Step step)
    {
        var item = _steps.FirstOrDefault(i => i.Model.Id == step.Id);
        if (item is null) return;
        _steps.Remove(item);
        _recordedCount--;
        _bar?.SetStepCount(_recordedCount);
    }

    // ---------------- editing ----------------

    private void OnListKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Delete) { OnDeleteStep(sender, e); e.Handled = true; }
        else if (e.Key == Key.Up && Keyboard.Modifiers == ModifierKeys.Control) { Move(-1); e.Handled = true; }
        else if (e.Key == Key.Down && Keyboard.Modifiers == ModifierKeys.Control) { Move(+1); e.Handled = true; }
    }

    private void OnMoveUp(object sender, RoutedEventArgs e) => Move(-1);
    private void OnMoveDown(object sender, RoutedEventArgs e) => Move(+1);

    private void Move(int delta)
    {
        if (Selected is not { } item) return;
        int from = _steps.IndexOf(item), to = from + delta;
        if (to < 0 || to >= _steps.Count) return;
        _steps.Move(from, to);
        StepList.SelectedItem = item;
        StepList.ScrollIntoView(item);
        StepHeader.Text = $"STEP {item.Number} OF {_steps.Count}";
        SetDirty(true);
    }

    private void OnDeleteStep(object sender, RoutedEventArgs e)
    {
        if (Selected is not { } item) return;
        int index = _steps.IndexOf(item);
        _steps.Remove(item);
        if (_steps.Count > 0) StepList.SelectedIndex = Math.Min(index, _steps.Count - 1);
        SetDirty(true);
    }

    private void OnAddNote(object sender, RoutedEventArgs e)
    {
        var item = new StepItem(new Step { Kind = StepKind.Note, Caption = "New step" });
        int index = Selected is { } s ? _steps.IndexOf(s) + 1 : _steps.Count;
        _steps.Insert(index, item);
        StepList.SelectedItem = item;
        CaptionBox.Focus();
        CaptionBox.SelectAll();
        SetDirty(true);
    }

    private void OnRemoveImage(object sender, RoutedEventArgs e)
    {
        if (Selected is not { } item) return;
        item.SetImage(null);
        SetDirty(true);
    }

    private void OnUndoImage(object sender, RoutedEventArgs e)
    {
        Selected?.UndoImage();
        ClearSelection();
        SetDirty(true);
    }

    // ---- selection rectangle on the screenshot ----

    private void OnSelectStart(object sender, MouseButtonEventArgs e)
    {
        _selectStart = e.GetPosition(SelectionCanvas);
        SelectionCanvas.CaptureMouse();
        UpdateSelectionRect(_selectStart.Value);
        SelectionRect.Visibility = Visibility.Visible;
    }

    private void OnSelectMove(object sender, MouseEventArgs e)
    {
        if (_selectStart is null) return;
        UpdateSelectionRect(e.GetPosition(SelectionCanvas));
    }

    private void OnSelectEnd(object sender, MouseButtonEventArgs e)
    {
        if (_selectStart is null) return;
        SelectionCanvas.ReleaseMouseCapture();
        _selectStart = null;
        bool hasSelection = SelectionRect.Width > 4 && SelectionRect.Height > 4;
        if (!hasSelection) SelectionRect.Visibility = Visibility.Collapsed;
        BlurButton.IsEnabled = CropButton.IsEnabled = hasSelection;
    }

    private void UpdateSelectionRect(WpfPoint current)
    {
        var start = _selectStart!.Value;
        double w = SelectionCanvas.ActualWidth, h = SelectionCanvas.ActualHeight;
        double x1 = Math.Clamp(Math.Min(start.X, current.X), 0, w), y1 = Math.Clamp(Math.Min(start.Y, current.Y), 0, h);
        double x2 = Math.Clamp(Math.Max(start.X, current.X), 0, w), y2 = Math.Clamp(Math.Max(start.Y, current.Y), 0, h);
        Canvas.SetLeft(SelectionRect, x1);
        Canvas.SetTop(SelectionRect, y1);
        SelectionRect.Width = x2 - x1;
        SelectionRect.Height = y2 - y1;
    }

    private void ClearSelection()
    {
        _selectStart = null;
        SelectionRect.Visibility = Visibility.Collapsed;
        SelectionRect.Width = SelectionRect.Height = 0;
        BlurButton.IsEnabled = CropButton.IsEnabled = false;
    }

    /// <summary>The selection rectangle converted from screen layout units to image pixels.</summary>
    private DrawingRect? SelectionInPixels()
    {
        if (Selected is not { HasImage: true } item || SelectionRect.Visibility != Visibility.Visible) return null;
        var (pw, ph) = item.PixelSize;
        double sx = pw / SelectionCanvas.ActualWidth, sy = ph / SelectionCanvas.ActualHeight;
        return new DrawingRect(
            (int)Math.Round(Canvas.GetLeft(SelectionRect) * sx), (int)Math.Round(Canvas.GetTop(SelectionRect) * sy),
            (int)Math.Round(SelectionRect.Width * sx), (int)Math.Round(SelectionRect.Height * sy));
    }

    private void OnBlur(object sender, RoutedEventArgs e) => EditImage(Annotator.Pixelate);
    private void OnCrop(object sender, RoutedEventArgs e) => EditImage(Annotator.Crop);

    private void EditImage(Func<byte[], DrawingRect, byte[]> edit)
    {
        if (Selected is not { } item || SelectionInPixels() is not { } region) return;
        item.SetImage(edit(item.Model.ImagePng!, region));
        ClearSelection();
        SetDirty(true);
    }

    // ---------------- files ----------------

    private void OnNew(object sender, RoutedEventArgs e)
    {
        if (_recorder is not null || !ConfirmDiscard()) return;
        LoadGuide(new Guide(), null);
    }

    private void OnOpen(object sender, RoutedEventArgs e)
    {
        if (_recorder is not null || !ConfirmDiscard()) return;
        var dlg = new OpenFileDialog { Filter = $"StepCapture guide (*{GuideFile.Extension})|*{GuideFile.Extension}" };
        if (dlg.ShowDialog(this) == true) OpenFile(dlg.FileName);
    }

    public void OpenFile(string path)
    {
        try
        {
            LoadGuide(GuideFile.Load(path), path);
        }
        catch (Exception ex)
        {
            Log.Error("Open " + path, ex);
            MessageBox.Show(this, $"Could not open {Path.GetFileName(path)}:\n{ex.Message}", "StepCapture",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void OnFileDrop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(DataFormats.FileDrop) is string[] { Length: > 0 } files
            && files[0].EndsWith(GuideFile.Extension, StringComparison.OrdinalIgnoreCase)
            && _recorder is null && ConfirmDiscard())
            OpenFile(files[0]);
    }

    private void OnSave(object sender, RoutedEventArgs e) => Save(saveAs: false);
    private void OnSaveAs(object sender, RoutedEventArgs e) => Save(saveAs: true);

    private bool Save(bool saveAs)
    {
        string? path = _filePath;
        if (saveAs || path is null)
        {
            var dlg = new SaveFileDialog
            {
                Filter = $"StepCapture guide (*{GuideFile.Extension})|*{GuideFile.Extension}",
                FileName = SafeFileName(TitleBox.Text) + GuideFile.Extension,
            };
            if (dlg.ShowDialog(this) != true) return false;
            path = dlg.FileName;
        }

        try
        {
            GuideFile.Save(SyncGuide(), path);
            _filePath = path;
            SetDirty(false);
            return true;
        }
        catch (Exception ex)
        {
            Log.Error("Save " + path, ex);
            MessageBox.Show(this, "Could not save: " + ex.Message, "StepCapture", MessageBoxButton.OK, MessageBoxImage.Error);
            return false;
        }
    }

    private void OnExportMenu(object sender, RoutedEventArgs e)
    {
        ExportMenu.PlacementTarget = ExportButton;
        ExportMenu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
        ExportMenu.IsOpen = true;
    }

    private async void OnExport(object sender, RoutedEventArgs e)
    {
        if (((MenuItem)sender).Tag is not IGuideExporter exporter || _steps.Count == 0) return;
        var dlg = new SaveFileDialog
        {
            Filter = $"{exporter.Name} (*{exporter.Extension})|*{exporter.Extension}",
            FileName = SafeFileName(TitleBox.Text) + exporter.Extension,
        };
        if (dlg.ShowDialog(this) != true) return;

        var guide = SyncGuide();
        string path = dlg.FileName;
        Mouse.OverrideCursor = Cursors.Wait;
        StatusText.Text = "Exporting…";
        try
        {
            await Task.Run(() => exporter.Export(guide, path));
        }
        catch (Exception ex)
        {
            Log.Error("Export " + path, ex);
            MessageBox.Show(this, "Export failed: " + ex.Message, "StepCapture", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }
        finally
        {
            Mouse.OverrideCursor = null;
            SetDirty(_dirty);
        }

        if (MessageBox.Show(this, $"Exported to {Path.GetFileName(path)}.\n\nOpen it now?", "Export complete",
                MessageBoxButton.YesNo, MessageBoxImage.Information) == MessageBoxResult.Yes)
        {
            try { Process.Start(new ProcessStartInfo(path) { UseShellExecute = true }); }
            catch (Exception ex) { Log.Error("Open export", ex); }
        }
    }

    private static string SafeFileName(string title)
    {
        string name = string.IsNullOrWhiteSpace(title) ? "Guide" : title.Trim();
        foreach (char c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '-');
        return name.Length > 80 ? name[..80] : name;
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (_recorder is not null)
        {
            _recorder.Stop();
            CleanupRecording();
        }
        if (!ConfirmDiscard()) e.Cancel = true;
        base.OnClosing(e);
    }
}

internal sealed class RelayCommand : ICommand
{
    private readonly Action _run;
    public RelayCommand(Action run) => _run = run;
    public event EventHandler? CanExecuteChanged { add { } remove { } }
    public bool CanExecute(object? parameter) => true;
    public void Execute(object? parameter) => _run();
}
