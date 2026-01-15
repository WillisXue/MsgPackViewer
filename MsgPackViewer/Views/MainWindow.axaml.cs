using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using AvaloniaEdit;
using AvaloniaEdit.Document;
using AvaloniaEdit.Rendering;
using MsgPackViewer.Models;
using MsgPackViewer.ViewModels;

namespace MsgPackViewer.Views;

public partial class MainWindow : Window
{
    private MainWindowViewModel? _viewModel;
    private bool _isUpdatingSelection;
    private HexHighlightTransformer? _hexHighlighter;
    private JsonHighlightTransformer? _jsonHighlighter;

    public MainWindow()
    {
        InitializeComponent();
        
        Loaded += OnLoaded;
    }

    private void OnLoaded(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        _viewModel = DataContext as MainWindowViewModel;
        if (_viewModel == null) return;
        
        _viewModel.OpenFileDialogAsync = OpenFileDialogAsync;
        _viewModel.SaveFileDialogAsync = SaveFileDialogAsync;
        _viewModel.DataLoaded += OnDataLoaded;
        
        _hexHighlighter = new HexHighlightTransformer();
        _jsonHighlighter = new JsonHighlightTransformer();
        
        HexEditor.TextArea.TextView.LineTransformers.Add(_hexHighlighter);
        JsonEditor.TextArea.TextView.LineTransformers.Add(_jsonHighlighter);
        
        JsonEditor.TextChanged += OnJsonEditorTextChanged;
        JsonEditor.TextArea.Caret.PositionChanged += OnJsonCaretPositionChanged;
        HexEditor.TextArea.Caret.PositionChanged += OnHexCaretPositionChanged;
        
        KeyDown += OnKeyDown;
    }

    private void OnDataLoaded()
    {
        if (_viewModel == null) return;
        
        _isUpdatingSelection = true;
        JsonEditor.Document = new TextDocument(_viewModel.JsonText);
        HexEditor.Document = new TextDocument(_viewModel.HexText);
        
        // Re-add line transformers after document change
        if (_jsonHighlighter != null && !JsonEditor.TextArea.TextView.LineTransformers.Contains(_jsonHighlighter))
            JsonEditor.TextArea.TextView.LineTransformers.Add(_jsonHighlighter);
        if (_hexHighlighter != null && !HexEditor.TextArea.TextView.LineTransformers.Contains(_hexHighlighter))
            HexEditor.TextArea.TextView.LineTransformers.Add(_hexHighlighter);
        
        _isUpdatingSelection = false;
        
        _hexHighlighter?.ClearHighlight();
        _jsonHighlighter?.ClearHighlight();
        HexEditor.TextArea.TextView.Redraw();
        JsonEditor.TextArea.TextView.Redraw();
    }

    private void OnJsonEditorTextChanged(object? sender, EventArgs e)
    {
        if (_isUpdatingSelection || _viewModel == null) return;
        _viewModel.JsonText = JsonEditor.Text;
        _viewModel.OnJsonTextChanged();
    }

    private void OnJsonCaretPositionChanged(object? sender, EventArgs e)
    {
        if (_isUpdatingSelection || _viewModel == null || _viewModel.AllNodes.Count == 0) return;
        
        _isUpdatingSelection = true;
        try
        {
            int offset = JsonEditor.CaretOffset;
            var node = _viewModel.FindNodeByJsonPosition(offset);
            if (node != null)
            {
                HighlightHexRange(node.StartOffset, node.EndOffset);
            }
        }
        finally
        {
            _isUpdatingSelection = false;
        }
    }

    private void OnHexCaretPositionChanged(object? sender, EventArgs e)
    {
        if (_isUpdatingSelection || _viewModel == null || _viewModel.AllNodes.Count == 0) return;
        
        _isUpdatingSelection = true;
        try
        {
            int caretOffset = HexEditor.CaretOffset;
            int? byteOffset = HexPositionToByteOffset(caretOffset);
            if (byteOffset.HasValue)
            {
                var node = _viewModel.FindNodeByHexOffset(byteOffset.Value);
                if (node != null)
                {
                    HighlightJsonRange(node.JsonStartIndex, node.JsonEndIndex);
                    HighlightHexRange(node.StartOffset, node.EndOffset);
                }
            }
        }
        finally
        {
            _isUpdatingSelection = false;
        }
    }

    private int? HexPositionToByteOffset(int textOffset)
    {
        var text = HexEditor.Text;
        if (string.IsNullOrEmpty(text) || textOffset >= text.Length) return null;
        
        var lines = text.Split('\n');
        int currentOffset = 0;
        int lineIndex = 0;
        
        foreach (var line in lines)
        {
            if (currentOffset + line.Length >= textOffset)
            {
                int posInLine = textOffset - currentOffset;
                if (posInLine > 10 && posInLine < 58)
                {
                    int hexPartPos = posInLine - 10;
                    int byteInLine = hexPartPos / 3;
                    if (hexPartPos >= 25) byteInLine--;
                    return lineIndex * 16 + Math.Min(byteInLine, 15);
                }
                return lineIndex * 16;
            }
            currentOffset += line.Length + 1;
            lineIndex++;
        }
        return null;
    }

    private void HighlightHexRange(int startByte, int endByte)
    {
        if (_hexHighlighter == null) return;
        
        var ranges = new List<(int start, int end)>();
        for (int b = startByte; b < endByte; b++)
        {
            int line = b / 16;
            int col = b % 16;
            int lineStart = line * 61;
            int hexStart = lineStart + 10 + col * 3 + (col >= 8 ? 1 : 0);
            ranges.Add((hexStart, hexStart + 2));
        }
        
        _hexHighlighter.SetHighlightRanges(ranges);
        HexEditor.TextArea.TextView.Redraw();
    }

    private void HighlightJsonRange(int startIndex, int endIndex)
    {
        if (_jsonHighlighter == null) return;
        
        int jsonLen = JsonEditor.Text.Length;
        
        int formattedStart = MapCompactToFormattedPosition(startIndex);
        int formattedEnd = MapCompactToFormattedPosition(endIndex);
        
        formattedStart = Math.Clamp(formattedStart, 0, jsonLen);
        formattedEnd = Math.Clamp(formattedEnd, 0, jsonLen);
        
        _jsonHighlighter.SetHighlightRange(formattedStart, formattedEnd);
        JsonEditor.TextArea.TextView.Redraw();
    }

    private int MapCompactToFormattedPosition(int compactPos)
    {
        return compactPos;
    }

    private async Task<IStorageFile?> OpenFileDialogAsync()
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Open MsgPack File",
            AllowMultiple = false,
            FileTypeFilter = new[]
            {
                new FilePickerFileType("MsgPack Files") { Patterns = new[] { "*.msgpack", "*.mp", "*.bin" } },
                new FilePickerFileType("All Files") { Patterns = new[] { "*" } }
            }
        });
        return files.FirstOrDefault();
    }

    private async Task<IStorageFile?> SaveFileDialogAsync()
    {
        return await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Save MsgPack File",
            DefaultExtension = ".msgpack",
            FileTypeChoices = new[]
            {
                new FilePickerFileType("MsgPack Files") { Patterns = new[] { "*.msgpack" } },
                new FilePickerFileType("All Files") { Patterns = new[] { "*" } }
            }
        });
    }

    private void OnExitClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        Close();
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyModifiers == KeyModifiers.Control)
        {
            if (e.Key == Key.O)
            {
                _viewModel?.OpenFileCommand.Execute(null);
                e.Handled = true;
            }
            else if (e.Key == Key.S)
            {
                if (e.KeyModifiers.HasFlag(KeyModifiers.Shift))
                    _viewModel?.SaveFileAsCommand.Execute(null);
                else
                    _viewModel?.SaveFileCommand.Execute(null);
                e.Handled = true;
            }
        }
    }
}

public class HexHighlightTransformer : DocumentColorizingTransformer
{
    private List<(int start, int end)> _highlightRanges = new();
    private readonly IBrush _highlightBrush = new SolidColorBrush(Color.FromRgb(255, 255, 150));

    public void SetHighlightRanges(List<(int start, int end)> ranges)
    {
        _highlightRanges = ranges;
    }

    public void ClearHighlight()
    {
        _highlightRanges.Clear();
    }

    protected override void ColorizeLine(DocumentLine line)
    {
        int lineStart = line.Offset;
        int lineEnd = line.EndOffset;
        
        foreach (var (start, end) in _highlightRanges)
        {
            if (start < lineEnd && end > lineStart)
            {
                int highlightStart = Math.Max(start, lineStart);
                int highlightEnd = Math.Min(end, lineEnd);
                
                ChangeLinePart(highlightStart, highlightEnd, element =>
                {
                    element.BackgroundBrush = _highlightBrush;
                });
            }
        }
    }
}

public class JsonHighlightTransformer : DocumentColorizingTransformer
{
    private int _highlightStart = -1;
    private int _highlightEnd = -1;
    private readonly IBrush _highlightBrush = new SolidColorBrush(Color.FromRgb(255, 255, 150));

    public void SetHighlightRange(int start, int end)
    {
        _highlightStart = start;
        _highlightEnd = end;
    }

    public void ClearHighlight()
    {
        _highlightStart = -1;
        _highlightEnd = -1;
    }

    protected override void ColorizeLine(DocumentLine line)
    {
        if (_highlightStart < 0 || _highlightEnd < 0) return;
        
        int lineStart = line.Offset;
        int lineEnd = line.EndOffset;
        
        if (_highlightStart < lineEnd && _highlightEnd > lineStart)
        {
            int highlightStart = Math.Max(_highlightStart, lineStart);
            int highlightEnd = Math.Min(_highlightEnd, lineEnd);
            
            ChangeLinePart(highlightStart, highlightEnd, element =>
            {
                element.BackgroundBrush = _highlightBrush;
            });
        }
    }
}