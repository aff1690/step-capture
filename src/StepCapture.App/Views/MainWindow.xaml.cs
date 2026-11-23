using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.Win32;
using StepCapture.App.Imaging;
using StepCapture.App.Native;
using StepCapture.App.Recording;
using StepCapture.App.Themes;
using StepCapture.Core.Export;
using StepCapture.Core.Model;
using StepCapture.Core.Recording;
using StepCapture.Core.Storage;

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

    public MainWindow()
    {
        InitializeComponent();
        StepList.ItemsSource = _steps;
        _steps.CollectionChanged += (_, _) => { Renumber(); UpdateViewState(); };

        BuildExportMenu();

        InputBindings.Add(new KeyBinding(new RelayCommand(() => OnNew(this, new RoutedEventArgs())), Key.N, ModifierKeys.Control));
        InputBindings.Add(new KeyBinding(new RelayCommand(() => OnOpen(this, new RoutedEventArgs())), Key.O, ModifierKeys.Control));
        InputBindings.Add(new KeyBinding(new RelayCommand(() => OnSave(this, new RoutedEventArgs())), Key.S, ModifierKeys.Control));
        InputBindings.Add(new KeyBinding(new RelayCommand(() => { if (_recorder is null) PasteScreenshot(showError: true); }),
            Key.V, ModifierKeys.Control | ModifierKeys.Shift));
        PreviewKeyDown += OnWindowKeyDown;
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

    /// <summary>Numbers regular steps 1, 2, 3...; overview steps don't count.</summary>
    private void Renumber()
    {
        var numbers = Guide.Numbers(_steps.Select(i => i.Model));
        for (int i = 0; i < _steps.Count; i++) _steps[i].Number = numbers[i] ?? 0;
        UpdateStepHeader();
    }

    private void UpdateStepHeader()
    {
        if (Selected is not { } s) return;
        int numbered = _steps.Count(i => !i.IsOverview);
        StepHeader.Text = s.IsOverview ? "OVERVIEW" : $"STEP {s.Number} OF {numbered}";
    }

    private void OnOverviewToggled(object sender, RoutedEventArgs e)
    {
        Renumber();
        SetDirty(true);
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
        UpdateStepHeader();
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
        _recorder.ScreenshotTaken += () => _bar?.FlashCaptured();
        _recorder.PauseHotkeyPressed += TogglePause;
        _recorder.StopHotkeyPressed += StopRecording;

        _bar = new RecordingBar();
        _bar.ScreenshotClicked += () =>
        {
            try { _recorder?.CaptureScreenshot(fromBar: true); }
            catch (Exception ex) { Log.Error("Screenshot", ex); }
        };
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

    /// <summary>Markup tool shortcuts (A, R, T, Ctrl+Z...) when the user isn't typing in a text box.</summary>
    private void OnWindowKeyDown(object sender, KeyEventArgs e)
    {
        if (Keyboard.FocusedElement is TextBox || _recorder is not null || Selected is null) return;
        if (Editor.HandleShortcut(e)) e.Handled = true;
    }

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
        UpdateStepHeader();
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

    private void OnScreenshotEdited() => SetDirty(true);

    // ---------------- screenshots without recording ----------------

    private void OnAddScreenshotMenu(object sender, RoutedEventArgs e)
    {
        BuildAddShotMenu();
        AddShotMenu.PlacementTarget = AddShotButton;
        AddShotMenu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
        AddShotMenu.IsOpen = true;
    }

    /// <summary>
    /// Lists one "capture in 3 seconds" entry per monitor (numbered left to right), plus paste and import.
    /// Rebuilt each time so plugging in a screen shows up straight away.
    /// </summary>
    private void BuildAddShotMenu()
    {
        AddShotMenu.Items.Clear();
        var monitors = ScreenCapture.Monitors();
        if (monitors.Count <= 1)
        {
            var only = new MenuItem { Header = "Capture the screen in 3 seconds" };
            only.Click += (_, _) => CaptureTimed(monitors.Count == 1 ? monitors[0] : null);
            AddShotMenu.Items.Add(only);
        }
        else
        {
            AddShotMenu.Items.Add(new MenuItem { Header = "Capture in 3 seconds:", IsEnabled = false });
            foreach (var m in monitors)
            {
                var item = new MenuItem { Header = "      " + m.Label, ToolTip = "Screens are numbered from left to right" };
                item.Click += (_, _) => CaptureTimed(m);
                AddShotMenu.Items.Add(item);
            }
            var underPointer = new MenuItem { Header = "      Screen under the mouse pointer",
                ToolTip = "Move the pointer to the screen you want during the countdown" };
            underPointer.Click += (_, _) => CaptureTimed(null);
            AddShotMenu.Items.Add(underPointer);
        }
        AddShotMenu.Items.Add(new Separator());
        var paste = new MenuItem { Header = "Paste from clipboard", InputGestureText = "Ctrl+Shift+V" };
        paste.Click += OnPasteScreenshot;
        AddShotMenu.Items.Add(paste);
        var file = new MenuItem { Header = "From an image file…" };
        file.Click += OnImportImage;
        AddShotMenu.Items.Add(file);
    }

    /// <summary>
    /// Hides StepCapture, waits 3 seconds so you can set the screen up, then captures the chosen monitor
    /// (or the one under the pointer when <paramref name="monitor"/> is null).
    /// </summary>
    private async void CaptureTimed(MonitorChoice? monitor)
    {
        if (_recorder is not null) return;
        var previous = WindowState == WindowState.Minimized ? WindowState.Normal : WindowState;
        WindowState = WindowState.Minimized;
        try
        {
            await Task.Delay(3000);
            byte[] png;
            string? app;
            CapturedScreen shot;
            if (monitor is not null)
                shot = ScreenCapture.Capture(monitor);
            else
            {
                NativeMethods.GetCursorPos(out var pt);
                shot = ScreenCapture.CaptureMonitorAt(pt.X, pt.Y);
            }
            using (shot)
                png = Annotator.RenderPlain(shot, drawCursor: false);
            app = await Task.Run(() => ElementInspector.Focused().Info?.AppName);
            AddScreenshotStep(png, CaptionBuilder.ForScreenshot(app), app);
        }
        catch (Exception ex)
        {
            Log.Error("Timed screenshot", ex);
            MessageBox.Show(this, "Could not capture the screen: " + ex.Message, "StepCapture", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            WindowState = previous;
            Activate();
        }
    }

    private void OnPasteScreenshot(object sender, RoutedEventArgs e) => PasteScreenshot(showError: true);

    /// <summary>Adds the image on the clipboard (for example from Snipping Tool or Print Screen) as an overview step.</summary>
    private bool PasteScreenshot(bool showError)
    {
        try
        {
            byte[]? png = null;
            if (Clipboard.ContainsImage() && Clipboard.GetImage() is { } bmp)
                png = ImageFiles.EncodePng(bmp);
            else if (Clipboard.ContainsFileDropList())
            {
                foreach (string? f in Clipboard.GetFileDropList())
                    if (f is not null && ImageFiles.IsImage(f)) { png = ImageFiles.LoadAsPng(f); break; }
            }

            if (png is null)
            {
                if (showError)
                    MessageBox.Show(this, "There's no picture on the clipboard. Copy a screenshot first (for example with Win+Shift+S).",
                        "Paste screenshot", MessageBoxButton.OK, MessageBoxImage.Information);
                return false;
            }
            AddScreenshotStep(png, "Overview", null);
            return true;
        }
        catch (Exception ex)
        {
            Log.Error("Paste screenshot", ex);
            if (showError)
                MessageBox.Show(this, "Could not paste the picture: " + ex.Message, "Paste screenshot", MessageBoxButton.OK, MessageBoxImage.Error);
            return false;
        }
    }

    private void OnImportImage(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog
        {
            Title = "Add screenshots",
            Filter = "Images (*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.tif;*.tiff)|*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.tif;*.tiff",
            Multiselect = true,
        };
        if (dlg.ShowDialog(this) == true) ImportImages(dlg.FileNames);
    }

    private void ImportImages(IEnumerable<string> files)
    {
        foreach (var f in files)
        {
            try
            {
                AddScreenshotStep(ImageFiles.LoadAsPng(f), Path.GetFileNameWithoutExtension(f), null);
            }
            catch (Exception ex)
            {
                Log.Error("Import image " + f, ex);
                MessageBox.Show(this, $"Could not read {Path.GetFileName(f)}: {ex.Message}", "Add screenshot",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }

    /// <summary>Inserts an overview step with the picture after the selected step (or at the end) and selects it.</summary>
    private void AddScreenshotStep(byte[] png, string caption, string? appName)
    {
        var item = new StepItem(new Step
        {
            Kind = StepKind.Screenshot,
            IsOverview = true,
            Caption = caption,
            AppName = appName ?? "",
            ImagePng = png,
        });
        int index = Selected is { } s ? _steps.IndexOf(s) + 1 : _steps.Count;
        _steps.Insert(index, item);
        StepList.SelectedItem = item;
        StepList.ScrollIntoView(item);
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
        if (_recorder is not null || e.Data.GetData(DataFormats.FileDrop) is not string[] { Length: > 0 } files) return;
        if (files[0].EndsWith(GuideFile.Extension, StringComparison.OrdinalIgnoreCase))
        {
            if (ConfirmDiscard()) OpenFile(files[0]);
            return;
        }
        // Pictures dropped on the window become overview steps.
        var images = files.Where(ImageFiles.IsImage).ToList();
        if (images.Count > 0) ImportImages(images);
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

    // ---- export menu and letterhead ----

    private void BuildExportMenu()
    {
        ExportMenu.Items.Clear();
        foreach (var exporter in Exporters.All)
        {
            var item = new MenuItem { Header = exporter.Name + "  (" + exporter.Extension + ")", Tag = exporter };
            item.Click += OnExport;
            ExportMenu.Items.Add(item);
        }

        var settings = AppSettings.Current;
        bool hasTemplate = settings.LetterheadPath is { } p && File.Exists(p);
        bool useLetterhead = hasTemplate && settings.UseLetterhead;
        ExportMenu.Items.Add(new Separator { Margin = new Thickness(4) });
        ExportMenu.Items.Add(new MenuItem
        {
            Header = new TextBlock { Text = "PAGE STYLE (WORD, PDF, HTML)", FontSize = 11, Foreground = (System.Windows.Media.Brush)FindResource("MutedBrush") },
            IsEnabled = false,
        });
        var blank = new MenuItem { Header = (useLetterhead ? "      " : "✓   ") + "Blank page" };
        blank.Click += (_, _) => { settings.UseLetterhead = false; settings.Save(); BuildExportMenu(); };
        ExportMenu.Items.Add(blank);
        if (hasTemplate)
        {
            var letterhead = new MenuItem { Header = (useLetterhead ? "✓   " : "      ") + "Letterhead: " + Path.GetFileName(settings.LetterheadPath) };
            letterhead.Click += (_, _) => { settings.UseLetterhead = true; settings.Save(); BuildExportMenu(); };
            ExportMenu.Items.Add(letterhead);
        }
        var choose = new MenuItem { Header = "      " + (hasTemplate ? "Change letterhead template…" : "Choose letterhead template…") };
        choose.Click += (_, _) => ChooseLetterhead();
        ExportMenu.Items.Add(choose);
    }

    private void ChooseLetterhead()
    {
        var dlg = new OpenFileDialog
        {
            Title = "Choose a Word template with your letterhead",
            Filter = "Word templates and documents (*.dotx;*.docx)|*.dotx;*.docx",
        };
        if (dlg.ShowDialog(this) != true) return;
        try
        {
            var lh = Letterhead.Load(dlg.FileName);
            if (lh.HeaderImage is null && string.IsNullOrWhiteSpace(lh.FooterText))
                MessageBox.Show(this, "That file has no header picture or footer text, so the letterhead will only affect Word exports.",
                    "Letterhead", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            Log.Error("Load letterhead", ex);
            MessageBox.Show(this, "Could not read that template: " + ex.Message, "Letterhead", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }
        AppSettings.Current.LetterheadPath = dlg.FileName;
        AppSettings.Current.UseLetterhead = true;
        AppSettings.Current.Save();
        BuildExportMenu();
    }

    /// <summary>Options for the next export: the letterhead if it's switched on and the template can be read.</summary>
    private ExportOptions? CurrentExportOptions()
    {
        var s = AppSettings.Current;
        if (!s.UseLetterhead || s.LetterheadPath is not { } path) return null;
        if (!File.Exists(path))
        {
            MessageBox.Show(this, $"The letterhead template wasn't found:\n{path}\n\nExporting on a blank page instead.",
                "Letterhead", MessageBoxButton.OK, MessageBoxImage.Warning);
            return null;
        }
        return new ExportOptions { Letterhead = Letterhead.Load(path) };
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
        ExportOptions? options;
        try { options = CurrentExportOptions(); }
        catch (Exception ex)
        {
            Log.Error("Load letterhead", ex);
            MessageBox.Show(this, "Could not read the letterhead template: " + ex.Message, "Letterhead", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }
        Mouse.OverrideCursor = Cursors.Wait;
        StatusText.Text = "Exporting…";
        try
        {
            await Task.Run(() => exporter.Export(guide, path, options));
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
