using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MsgPackViewer.Models;

namespace MsgPackViewer.ViewModels;

public partial class MainWindowViewModel : ViewModelBase
{
    private readonly MsgPackParser _parser = new();
    
    [ObservableProperty]
    private string _jsonText = string.Empty;
    
    [ObservableProperty]
    private string _hexText = string.Empty;
    
    [ObservableProperty]
    private string _statusText = "Ready";
    
    [ObservableProperty]
    private string? _currentFilePath;
    
    [ObservableProperty]
    private bool _isModified;
    
    [ObservableProperty]
    private int _selectedHexStart = -1;
    
    [ObservableProperty]
    private int _selectedHexEnd = -1;
    
    private byte[] _rawData = Array.Empty<byte>();
    private MsgPackNode? _rootNode;
    private List<MsgPackNode> _allNodes = new();
    private int[] _jsonPositionMap = Array.Empty<int>();
    
    public byte[] RawData => _rawData;
    public List<MsgPackNode> AllNodes => _allNodes;
    public int[] JsonPositionMap => _jsonPositionMap;
    
    public event Action? DataLoaded;
    
    public Func<Task<IStorageFile?>>? OpenFileDialogAsync { get; set; }
    public Func<Task<IStorageFile?>>? SaveFileDialogAsync { get; set; }

    public void LoadFromBytes(byte[] data, string? filePath = null)
    {
        try
        {
            if (data == null || data.Length == 0)
            {
                StatusText = "Error: Empty file";
                return;
            }
            
            _rawData = data;
            var (json, rootNode, allNodes) = _parser.Parse(data);
            _rootNode = rootNode;
            _allNodes = allNodes;
            
            var (formattedJson, positionMap) = MsgPackParser.FormatJsonWithMap(json);
            JsonText = formattedJson;
            _jsonPositionMap = positionMap;
            HexText = MsgPackParser.FormatHex(data);
            CurrentFilePath = filePath;
            IsModified = false;
            StatusText = filePath != null ? $"Loaded: {filePath} ({data.Length} bytes)" : $"Loaded from bytes ({data.Length} bytes)";
            
            DataLoaded?.Invoke();
        }
        catch (Exception ex)
        {
            StatusText = $"Error: {ex.Message}";
            HexText = MsgPackParser.FormatHex(data);
            JsonText = $"// Parse error: {ex.Message}\n// Raw hex displayed on the right";
            DataLoaded?.Invoke();
        }
    }

    [RelayCommand]
    private async Task OpenFileAsync()
    {
        if (OpenFileDialogAsync == null) return;
        
        var file = await OpenFileDialogAsync();
        if (file != null)
        {
            var path = file.Path.LocalPath;
            var data = await File.ReadAllBytesAsync(path);
            LoadFromBytes(data, path);
        }
    }

    [RelayCommand]
    private async Task SaveFileAsync()
    {
        if (string.IsNullOrEmpty(CurrentFilePath))
        {
            await SaveFileAsAsync();
            return;
        }
        
        await SaveToFileAsync(CurrentFilePath);
    }

    [RelayCommand]
    private async Task SaveFileAsAsync()
    {
        if (SaveFileDialogAsync == null) return;
        
        var file = await SaveFileDialogAsync();
        if (file != null)
        {
            await SaveToFileAsync(file.Path.LocalPath);
        }
    }

    private async Task SaveToFileAsync(string path)
    {
        try
        {
            var data = MsgPackParser.RebuildFromJson(JsonText, _rootNode, _rawData);
            await File.WriteAllBytesAsync(path, data);

            LoadFromBytes(data, path);
            StatusText = $"Saved: {path}";
        }
        catch (Exception ex)
        {
            StatusText = $"Save error: {ex.Message}";
        }
    }

    public void OnJsonTextChanged()
    {
        IsModified = true;
    }

    public MsgPackNode? FindNodeByJsonPosition(int formattedPosition)
    {
        MsgPackNode? best = null;
        foreach (var node in _allNodes)
        {
            int formattedStart = MapCompactToFormatted(node.JsonStartIndex);
            int formattedEnd = MapCompactToFormatted(node.JsonEndIndex);
            
            if (formattedPosition >= formattedStart && formattedPosition <= formattedEnd)
            {
                if (best == null || (node.JsonEndIndex - node.JsonStartIndex) < (best.JsonEndIndex - best.JsonStartIndex))
                {
                    best = node;
                }
            }
        }
        return best;
    }
    
    public int MapCompactToFormatted(int compactPos)
    {
        if (_jsonPositionMap.Length == 0) return compactPos;
        if (compactPos < 0) return 0;
        if (compactPos >= _jsonPositionMap.Length) return _jsonPositionMap[^1];
        return _jsonPositionMap[compactPos];
    }

    public MsgPackNode? FindNodeByHexOffset(int byteOffset)
    {
        MsgPackNode? best = null;
        foreach (var node in _allNodes)
        {
            if (byteOffset >= node.StartOffset && byteOffset < node.EndOffset)
            {
                if (best == null || (node.EndOffset - node.StartOffset) < (best.EndOffset - best.StartOffset))
                {
                    best = node;
                }
            }
        }
        return best;
    }
}