using System;
using System.Buffers;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;
using MessagePack;

namespace MsgPackViewer.Models;

public class MsgPackParser
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };
    
    private static readonly JsonSerializerOptions JsonFormatOptions = new()
    {
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };
    
    private byte[] _data = Array.Empty<byte>();
    private int _position;
    private readonly StringBuilder _jsonBuilder = new();
    private readonly List<MsgPackNode> _allNodes = new();

    public (string Json, MsgPackNode RootNode, List<MsgPackNode> AllNodes) Parse(byte[] data)
    {
        _data = data;
        _position = 0;
        _jsonBuilder.Clear();
        _allNodes.Clear();

        var rootNode = ParseValue("$");
        return (_jsonBuilder.ToString(), rootNode, _allNodes);
    }

    private MsgPackNode ParseValue(string path)
    {
        if (_position >= _data.Length)
            throw new InvalidOperationException("Unexpected end of data");

        int startOffset = _position;
        int jsonStart = _jsonBuilder.Length;
        byte b = _data[_position];

        MsgPackNode node;

        if (b <= 0x7f) // positive fixint
        {
            node = ParsePositiveFixInt(path);
        }
        else if (b >= 0xe0) // negative fixint
        {
            node = ParseNegativeFixInt(path);
        }
        else if ((b & 0xf0) == 0x80) // fixmap
        {
            node = ParseMap(path, b & 0x0f);
        }
        else if ((b & 0xf0) == 0x90) // fixarray
        {
            node = ParseArray(path, b & 0x0f);
        }
        else if ((b & 0xe0) == 0xa0) // fixstr
        {
            node = ParseString(path, b & 0x1f);
        }
        else
        {
            node = b switch
            {
                0xc0 => ParseNil(path),
                0xc2 => ParseBool(path, false),
                0xc3 => ParseBool(path, true),
                0xc4 => ParseBin8(path),
                0xc5 => ParseBin16(path),
                0xc6 => ParseBin32(path),
                0xc7 => ParseExt8(path),
                0xc8 => ParseExt16(path),
                0xc9 => ParseExt32(path),
                0xca => ParseFloat32(path),
                0xcb => ParseFloat64(path),
                0xcc => ParseUInt8(path),
                0xcd => ParseUInt16(path),
                0xce => ParseUInt32(path),
                0xcf => ParseUInt64(path),
                0xd0 => ParseInt8(path),
                0xd1 => ParseInt16(path),
                0xd2 => ParseInt32(path),
                0xd3 => ParseInt64(path),
                0xd4 => ParseFixExt(path, 1),
                0xd5 => ParseFixExt(path, 2),
                0xd6 => ParseFixExt(path, 4),
                0xd7 => ParseFixExt(path, 8),
                0xd8 => ParseFixExt(path, 16),
                0xd9 => ParseStr8(path),
                0xda => ParseStr16(path),
                0xdb => ParseStr32(path),
                0xdc => ParseArray16(path),
                0xdd => ParseArray32(path),
                0xde => ParseMap16(path),
                0xdf => ParseMap32(path),
                _ => throw new InvalidOperationException($"Unknown format: 0x{b:X2}")
            };
        }

        node.StartOffset = startOffset;
        node.EndOffset = _position;
        node.JsonStartIndex = jsonStart;
        node.JsonEndIndex = _jsonBuilder.Length;
        node.JsonPath = path;
        _allNodes.Add(node);

        return node;
    }

    private MsgPackNode ParsePositiveFixInt(string path)
    {
        byte value = _data[_position++];
        _jsonBuilder.Append(value);
        return new MsgPackNode { Value = (long)value, NodeType = MsgPackNodeType.Integer };
    }

    private MsgPackNode ParseNegativeFixInt(string path)
    {
        sbyte value = (sbyte)_data[_position++];
        _jsonBuilder.Append(value);
        return new MsgPackNode { Value = (long)value, NodeType = MsgPackNodeType.Integer };
    }

    private MsgPackNode ParseNil(string path)
    {
        _position++;
        _jsonBuilder.Append("null");
        return new MsgPackNode { Value = null, NodeType = MsgPackNodeType.Nil };
    }

    private MsgPackNode ParseBool(string path, bool value)
    {
        _position++;
        _jsonBuilder.Append(value ? "true" : "false");
        return new MsgPackNode { Value = value, NodeType = MsgPackNodeType.Boolean };
    }

    private MsgPackNode ParseFloat32(string path)
    {
        _position++;
        float value = BitConverter.ToSingle(ReadBigEndian(4), 0);
        _jsonBuilder.Append(value.ToString("G"));
        return new MsgPackNode { Value = value, NodeType = MsgPackNodeType.Float };
    }

    private MsgPackNode ParseFloat64(string path)
    {
        _position++;
        double value = BitConverter.ToDouble(ReadBigEndian(8), 0);
        _jsonBuilder.Append(value.ToString("G"));
        return new MsgPackNode { Value = value, NodeType = MsgPackNodeType.Float };
    }

    private MsgPackNode ParseUInt8(string path)
    {
        _position++;
        byte value = _data[_position++];
        _jsonBuilder.Append(value);
        return new MsgPackNode { Value = (long)value, NodeType = MsgPackNodeType.Integer };
    }

    private MsgPackNode ParseUInt16(string path)
    {
        _position++;
        ushort value = (ushort)((_data[_position] << 8) | _data[_position + 1]);
        _position += 2;
        _jsonBuilder.Append(value);
        return new MsgPackNode { Value = (long)value, NodeType = MsgPackNodeType.Integer };
    }

    private MsgPackNode ParseUInt32(string path)
    {
        _position++;
        uint value = (uint)((_data[_position] << 24) | (_data[_position + 1] << 16) |
                           (_data[_position + 2] << 8) | _data[_position + 3]);
        _position += 4;
        _jsonBuilder.Append(value);
        return new MsgPackNode { Value = (long)value, NodeType = MsgPackNodeType.Integer };
    }

    private MsgPackNode ParseUInt64(string path)
    {
        _position++;
        ulong value = ((ulong)_data[_position] << 56) | ((ulong)_data[_position + 1] << 48) |
                      ((ulong)_data[_position + 2] << 40) | ((ulong)_data[_position + 3] << 32) |
                      ((ulong)_data[_position + 4] << 24) | ((ulong)_data[_position + 5] << 16) |
                      ((ulong)_data[_position + 6] << 8) | _data[_position + 7];
        _position += 8;
        _jsonBuilder.Append(value);
        return new MsgPackNode { Value = value, NodeType = MsgPackNodeType.Integer };
    }

    private MsgPackNode ParseInt8(string path)
    {
        _position++;
        sbyte value = (sbyte)_data[_position++];
        _jsonBuilder.Append(value);
        return new MsgPackNode { Value = (long)value, NodeType = MsgPackNodeType.Integer };
    }

    private MsgPackNode ParseInt16(string path)
    {
        _position++;
        short value = (short)((_data[_position] << 8) | _data[_position + 1]);
        _position += 2;
        _jsonBuilder.Append(value);
        return new MsgPackNode { Value = (long)value, NodeType = MsgPackNodeType.Integer };
    }

    private MsgPackNode ParseInt32(string path)
    {
        _position++;
        int value = (_data[_position] << 24) | (_data[_position + 1] << 16) |
                    (_data[_position + 2] << 8) | _data[_position + 3];
        _position += 4;
        _jsonBuilder.Append(value);
        return new MsgPackNode { Value = (long)value, NodeType = MsgPackNodeType.Integer };
    }

    private MsgPackNode ParseInt64(string path)
    {
        _position++;
        long value = ((long)_data[_position] << 56) | ((long)_data[_position + 1] << 48) |
                     ((long)_data[_position + 2] << 40) | ((long)_data[_position + 3] << 32) |
                     ((long)_data[_position + 4] << 24) | ((long)_data[_position + 5] << 16) |
                     ((long)_data[_position + 6] << 8) | _data[_position + 7];
        _position += 8;
        _jsonBuilder.Append(value);
        return new MsgPackNode { Value = value, NodeType = MsgPackNodeType.Integer };
    }

    private MsgPackNode ParseString(string path, int length)
    {
        _position++;
        string value = Encoding.UTF8.GetString(_data, _position, length);
        _position += length;
        _jsonBuilder.Append(JsonSerializer.Serialize(value, JsonOptions));
        return new MsgPackNode { Value = value, NodeType = MsgPackNodeType.String };
    }

    private MsgPackNode ParseStr8(string path)
    {
        _position++;
        int length = _data[_position++];
        string value = Encoding.UTF8.GetString(_data, _position, length);
        _position += length;
        _jsonBuilder.Append(JsonSerializer.Serialize(value, JsonOptions));
        return new MsgPackNode { Value = value, NodeType = MsgPackNodeType.String };
    }

    private MsgPackNode ParseStr16(string path)
    {
        _position++;
        int length = (_data[_position] << 8) | _data[_position + 1];
        _position += 2;
        string value = Encoding.UTF8.GetString(_data, _position, length);
        _position += length;
        _jsonBuilder.Append(JsonSerializer.Serialize(value, JsonOptions));
        return new MsgPackNode { Value = value, NodeType = MsgPackNodeType.String };
    }

    private MsgPackNode ParseStr32(string path)
    {
        _position++;
        int length = (_data[_position] << 24) | (_data[_position + 1] << 16) |
                     (_data[_position + 2] << 8) | _data[_position + 3];
        _position += 4;
        string value = Encoding.UTF8.GetString(_data, _position, length);
        _position += length;
        _jsonBuilder.Append(JsonSerializer.Serialize(value, JsonOptions));
        return new MsgPackNode { Value = value, NodeType = MsgPackNodeType.String };
    }

    private MsgPackNode ParseBin8(string path)
    {
        _position++;
        int length = _data[_position++];
        byte[] value = new byte[length];
        Array.Copy(_data, _position, value, 0, length);
        _position += length;
        _jsonBuilder.Append('"');
        _jsonBuilder.Append(Convert.ToBase64String(value));
        _jsonBuilder.Append('"');
        return new MsgPackNode { Value = value, NodeType = MsgPackNodeType.Binary };
    }

    private MsgPackNode ParseBin16(string path)
    {
        _position++;
        int length = (_data[_position] << 8) | _data[_position + 1];
        _position += 2;
        byte[] value = new byte[length];
        Array.Copy(_data, _position, value, 0, length);
        _position += length;
        _jsonBuilder.Append('"');
        _jsonBuilder.Append(Convert.ToBase64String(value));
        _jsonBuilder.Append('"');
        return new MsgPackNode { Value = value, NodeType = MsgPackNodeType.Binary };
    }

    private MsgPackNode ParseBin32(string path)
    {
        _position++;
        int length = (_data[_position] << 24) | (_data[_position + 1] << 16) |
                     (_data[_position + 2] << 8) | _data[_position + 3];
        _position += 4;
        byte[] value = new byte[length];
        Array.Copy(_data, _position, value, 0, length);
        _position += length;
        _jsonBuilder.Append('"');
        _jsonBuilder.Append(Convert.ToBase64String(value));
        _jsonBuilder.Append('"');
        return new MsgPackNode { Value = value, NodeType = MsgPackNodeType.Binary };
    }

    private MsgPackNode ParseArray(string path, int count)
    {
        _position++;
        var node = new MsgPackNode { NodeType = MsgPackNodeType.Array };
        _jsonBuilder.Append('[');
        for (int i = 0; i < count; i++)
        {
            if (i > 0) _jsonBuilder.Append(',');
            node.Children.Add(ParseValue($"{path}[{i}]"));
        }
        _jsonBuilder.Append(']');
        return node;
    }

    private MsgPackNode ParseArray16(string path)
    {
        _position++;
        int count = (_data[_position] << 8) | _data[_position + 1];
        _position += 2;
        var node = new MsgPackNode { NodeType = MsgPackNodeType.Array };
        _jsonBuilder.Append('[');
        for (int i = 0; i < count; i++)
        {
            if (i > 0) _jsonBuilder.Append(',');
            node.Children.Add(ParseValue($"{path}[{i}]"));
        }
        _jsonBuilder.Append(']');
        return node;
    }

    private MsgPackNode ParseArray32(string path)
    {
        _position++;
        int count = (_data[_position] << 24) | (_data[_position + 1] << 16) |
                    (_data[_position + 2] << 8) | _data[_position + 3];
        _position += 4;
        var node = new MsgPackNode { NodeType = MsgPackNodeType.Array };
        _jsonBuilder.Append('[');
        for (int i = 0; i < count; i++)
        {
            if (i > 0) _jsonBuilder.Append(',');
            node.Children.Add(ParseValue($"{path}[{i}]"));
        }
        _jsonBuilder.Append(']');
        return node;
    }

    private MsgPackNode ParseMap(string path, int count)
    {
        _position++;
        var node = new MsgPackNode { NodeType = MsgPackNodeType.Map };
        _jsonBuilder.Append('{');
        for (int i = 0; i < count; i++)
        {
            if (i > 0) _jsonBuilder.Append(',');
            int keyJsonStart = _jsonBuilder.Length;
            var keyNode = ParseValue($"{path}.key{i}");
            string key = keyNode.Value?.ToString() ?? $"key{i}";
            // Ensure key is a valid JSON string
            if (keyNode.NodeType != MsgPackNodeType.String)
            {
                // Remove the non-string key and replace with quoted version
                _jsonBuilder.Length = keyJsonStart;
                _jsonBuilder.Append(JsonSerializer.Serialize(key, JsonOptions));
            }
            _jsonBuilder.Append(':');
            node.Children.Add(ParseValue($"{path}.{key}"));
        }
        _jsonBuilder.Append('}');
        return node;
    }

    private MsgPackNode ParseMap16(string path)
    {
        _position++;
        int count = (_data[_position] << 8) | _data[_position + 1];
        _position += 2;
        var node = new MsgPackNode { NodeType = MsgPackNodeType.Map };
        _jsonBuilder.Append('{');
        for (int i = 0; i < count; i++)
        {
            if (i > 0) _jsonBuilder.Append(',');
            int keyJsonStart = _jsonBuilder.Length;
            var keyNode = ParseValue($"{path}.key{i}");
            string key = keyNode.Value?.ToString() ?? $"key{i}";
            if (keyNode.NodeType != MsgPackNodeType.String)
            {
                _jsonBuilder.Length = keyJsonStart;
                _jsonBuilder.Append(JsonSerializer.Serialize(key, JsonOptions));
            }
            _jsonBuilder.Append(':');
            node.Children.Add(ParseValue($"{path}.{key}"));
        }
        _jsonBuilder.Append('}');
        return node;
    }

    private MsgPackNode ParseMap32(string path)
    {
        _position++;
        int count = (_data[_position] << 24) | (_data[_position + 1] << 16) |
                    (_data[_position + 2] << 8) | _data[_position + 3];
        _position += 4;
        var node = new MsgPackNode { NodeType = MsgPackNodeType.Map };
        _jsonBuilder.Append('{');
        for (int i = 0; i < count; i++)
        {
            if (i > 0) _jsonBuilder.Append(',');
            int keyJsonStart = _jsonBuilder.Length;
            var keyNode = ParseValue($"{path}.key{i}");
            string key = keyNode.Value?.ToString() ?? $"key{i}";
            if (keyNode.NodeType != MsgPackNodeType.String)
            {
                _jsonBuilder.Length = keyJsonStart;
                _jsonBuilder.Append(JsonSerializer.Serialize(key, JsonOptions));
            }
            _jsonBuilder.Append(':');
            node.Children.Add(ParseValue($"{path}.{key}"));
        }
        _jsonBuilder.Append('}');
        return node;
    }

    private MsgPackNode ParseFixExt(string path, int length)
    {
        _position++;
        sbyte type = (sbyte)_data[_position++];
        byte[] data = new byte[length];
        Array.Copy(_data, _position, data, 0, length);
        _position += length;
        _jsonBuilder.Append($"{{\"$ext\":{type},\"data\":\"{Convert.ToBase64String(data)}\"}}");
        return new MsgPackNode { Value = (type, data), NodeType = MsgPackNodeType.Extension };
    }

    private MsgPackNode ParseExt8(string path)
    {
        _position++;
        int length = _data[_position++];
        sbyte type = (sbyte)_data[_position++];
        byte[] data = new byte[length];
        Array.Copy(_data, _position, data, 0, length);
        _position += length;
        _jsonBuilder.Append($"{{\"$ext\":{type},\"data\":\"{Convert.ToBase64String(data)}\"}}");
        return new MsgPackNode { Value = (type, data), NodeType = MsgPackNodeType.Extension };
    }

    private MsgPackNode ParseExt16(string path)
    {
        _position++;
        int length = (_data[_position] << 8) | _data[_position + 1];
        _position += 2;
        sbyte type = (sbyte)_data[_position++];
        byte[] data = new byte[length];
        Array.Copy(_data, _position, data, 0, length);
        _position += length;
        _jsonBuilder.Append($"{{\"$ext\":{type},\"data\":\"{Convert.ToBase64String(data)}\"}}");
        return new MsgPackNode { Value = (type, data), NodeType = MsgPackNodeType.Extension };
    }

    private MsgPackNode ParseExt32(string path)
    {
        _position++;
        int length = (_data[_position] << 24) | (_data[_position + 1] << 16) |
                     (_data[_position + 2] << 8) | _data[_position + 3];
        _position += 4;
        sbyte type = (sbyte)_data[_position++];
        byte[] data = new byte[length];
        Array.Copy(_data, _position, data, 0, length);
        _position += length;
        _jsonBuilder.Append($"{{\"$ext\":{type},\"data\":\"{Convert.ToBase64String(data)}\"}}");
        return new MsgPackNode { Value = (type, data), NodeType = MsgPackNodeType.Extension };
    }

    private byte[] ReadBigEndian(int count)
    {
        byte[] result = new byte[count];
        for (int i = 0; i < count; i++)
        {
            result[count - 1 - i] = _data[_position + i];
        }
        _position += count;
        return result;
    }

    public static (string formattedJson, int[] positionMap) FormatJsonWithMap(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var options = new JsonSerializerOptions
            {
                WriteIndented = true,
                Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
            };
            string formatted = JsonSerializer.Serialize(doc.RootElement, options);
            
            // Build position map from compact to formatted
            int[] map = new int[json.Length + 1];
            int compactIdx = 0;
            int formattedIdx = 0;
            
            while (compactIdx < json.Length && formattedIdx < formatted.Length)
            {
                // Skip whitespace in formatted JSON
                while (formattedIdx < formatted.Length && 
                       (formatted[formattedIdx] == ' ' || formatted[formattedIdx] == '\n' || 
                        formatted[formattedIdx] == '\r' || formatted[formattedIdx] == '\t'))
                {
                    formattedIdx++;
                }
                
                if (formattedIdx < formatted.Length)
                {
                    map[compactIdx] = formattedIdx;
                    
                    // Handle string content (may differ due to encoding)
                    if (json[compactIdx] == formatted[formattedIdx])
                    {
                        compactIdx++;
                        formattedIdx++;
                    }
                    else
                    {
                        // Characters don't match, advance both
                        compactIdx++;
                        formattedIdx++;
                    }
                }
            }
            map[json.Length] = formatted.Length;
            
            return (formatted, map);
        }
        catch (Exception ex)
        {
            return ($"// JSON Parse Error: {ex.Message}\n{json}", Array.Empty<int>());
        }
    }
    
    public static string FormatJson(string json)
    {
        var (formatted, _) = FormatJsonWithMap(json);
        return formatted;
    }

    public static string FormatHex(byte[] data)
    {
        var sb = new StringBuilder();
        for (int i = 0; i < data.Length; i += 16)
        {
            sb.Append($"{i:X8}: ");
            for (int j = 0; j < 16; j++)
            {
                if (i + j < data.Length)
                    sb.Append($"{data[i + j]:X2} ");
                else
                    sb.Append("   ");
                if (j == 7) sb.Append(' ');
            }
            sb.Append(' ');
            for (int j = 0; j < 16 && i + j < data.Length; j++)
            {
                byte b = data[i + j];
                sb.Append(b >= 0x20 && b < 0x7f ? (char)b : '.');
            }
            sb.AppendLine();
        }
        return sb.ToString();
    }

    public static byte[] SerializeFromJson(string json)
    {
        var jsonDoc = JsonDocument.Parse(json);
        return MessagePackSerializer.ConvertFromJson(json);
    }
}
