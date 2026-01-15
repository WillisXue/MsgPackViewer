# MsgPackViewer

A cross-platform MessagePack file viewer and editor built with Avalonia UI.

![.NET](https://img.shields.io/badge/.NET-10.0-blue)
![Avalonia](https://img.shields.io/badge/Avalonia-11.3-purple)
![License](https://img.shields.io/badge/License-MIT-green)

## Features

- **Load & View** - Open and view MessagePack binary files (`.msgpack`, `.mp`, `.bin`, or any file)
- **Dual Panel View** - JSON view on the left, Hex view on the right
- **Edit & Save** - Modify JSON content and save back to MessagePack format
- **Bidirectional Highlighting** - Click on JSON to highlight corresponding Hex bytes, and vice versa
- **Full MsgPack Support** - Supports all MessagePack types including:
  - Integers (fixint, int8/16/32/64, uint8/16/32/64)
  - Floats (float32, float64)
  - Strings (fixstr, str8/16/32)
  - Binary (bin8/16/32)
  - Arrays (fixarray, array16/32)
  - Maps (fixmap, map16/32)
  - Nil, Boolean
  - Extension types

## Screenshot

```
┌─────────────────────────────────────────────────────────────┐
│  File: Open | Save | Save As | Exit                         │
├──────────────────────────────┬──────────────────────────────┤
│  {                           │  00000000: 82 A4 6E 61 6D 65 │
│    "name": "test",           │  00000010: A4 74 65 73 74 A5 │
│    "value": 123              │  00000020: 76 61 6C 75 65 7B │
│  }                           │                              │
├──────────────────────────────┴──────────────────────────────┤
│  Status: Loaded: example.msgpack (45 bytes)                 │
└─────────────────────────────────────────────────────────────┘
```

## Installation

### Prerequisites

- [.NET 10.0 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) or later

### Build from Source

```bash
git clone https://github.com/yourusername/MsgPackViewer.git
cd MsgPackViewer
dotnet build
```

### Run

```bash
dotnet run --project MsgPackViewer
```

## Usage

1. **Open File**: `File > Open` or `Ctrl+O` to open a MessagePack file
2. **View**: JSON is displayed on the left, Hex dump on the right
3. **Navigate**: Click on any element in JSON or Hex view to highlight the corresponding data
4. **Edit**: Modify the JSON content directly in the editor
5. **Save**: `File > Save` (`Ctrl+S`) or `File > Save As` (`Ctrl+Shift+S`)

## Keyboard Shortcuts

| Shortcut | Action |
|----------|--------|
| `Ctrl+O` | Open file |
| `Ctrl+S` | Save file |
| `Ctrl+Shift+S` | Save as |

## Technical Details

- **UI Framework**: [Avalonia UI](https://avaloniaui.net/) 11.3
- **Text Editor**: [AvaloniaEdit](https://github.com/AvaloniaUI/AvaloniaEdit)
- **MessagePack**: Custom parser with byte offset tracking + [MessagePack-CSharp](https://github.com/MessagePack-CSharp/MessagePack-CSharp) for serialization
- **Architecture**: MVVM with [CommunityToolkit.Mvvm](https://github.com/CommunityToolkit/dotnet)

## MessagePack Specification

This viewer implements the [MessagePack specification](https://github.com/msgpack/msgpack/blob/master/spec.md).

## License

MIT License

## Contributing

Contributions are welcome! Please feel free to submit a Pull Request.
