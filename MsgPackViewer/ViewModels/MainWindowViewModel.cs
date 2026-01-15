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
    
    public byte[] RawData => _rawData;
    public List<MsgPackNode> AllNodes => _allNodes;
    
    public event Action? DataLoaded;
    
    public Func<Task<IStorageFile?>>? OpenFileDialogAsync { get; set; }
    public Func<Task<IStorageFile?>>? SaveFileDialogAsync { get; set; }

    public void LoadFromBytes(byte[] data, string? filePath = null)
    {
        try
        {
            _rawData = data;
            var (json, rootNode, allNodes) = _parser.Parse(data);
            _rootNode = rootNode;
            _allNodes = allNodes;
            
            JsonText = MsgPackParser.FormatJson(json);
            HexText = MsgPackParser.FormatHex(data);
            CurrentFilePath = filePath;
            IsModified = false;
            StatusText = filePath != null ? $"Loaded: {filePath}" : "Loaded from bytes";
            
            DataLoaded?.Invoke();
        }
        catch (Exception ex)
        {
            StatusText = $"Error: {ex.Message}";
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
            var data = MsgPackParser.SerializeFromJson(JsonText);
            await File.WriteAllBytesAsync(path, data);
            
            _rawData = data;
            HexText = MsgPackParser.FormatHex(data);
            CurrentFilePath = path;
            IsModified = false;
            StatusText = $"Saved: {path}";
            
            var (_, rootNode, allNodes) = _parser.Parse(data);
            _rootNode = rootNode;
            _allNodes = allNodes;
            DataLoaded?.Invoke();
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

    public MsgPackNode? FindNodeByJsonPosition(int position)
    {
        MsgPackNode? best = null;
        foreach (var node in _allNodes)
        {
            if (position >= node.JsonStartIndex && position <= node.JsonEndIndex)
            {
                if (best == null || (node.JsonEndIndex - node.JsonStartIndex) < (best.JsonEndIndex - best.JsonStartIndex))
                {
                    best = node;
                }
            }
        }
        return best;
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