using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using MessagePack;

namespace MsgPackViewer.Models;

public class MsgPackParser
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        TypeInfoResolver = new DefaultJsonTypeInfoResolver()
    };
    
    private static readonly JsonSerializerOptions JsonFormatOptions = new()
    {
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        TypeInfoResolver = new DefaultJsonTypeInfoResolver()
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
        return new MsgPackNode { Value = (long)value, NodeType = MsgPackNodeType.Integer, NumberFormat = MsgPackNumberFormat.PositiveFixInt };
    }

    private MsgPackNode ParseNegativeFixInt(string path)
    {
        sbyte value = (sbyte)_data[_position++];
        _jsonBuilder.Append(value);
        return new MsgPackNode { Value = (long)value, NodeType = MsgPackNodeType.Integer, NumberFormat = MsgPackNumberFormat.NegativeFixInt };
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
        AppendFloatValue(value);
        return new MsgPackNode { Value = value, NodeType = MsgPackNodeType.Float, NumberFormat = MsgPackNumberFormat.Float32 };
    }

    private MsgPackNode ParseFloat64(string path)
    {
        _position++;
        double value = BitConverter.ToDouble(ReadBigEndian(8), 0);
        AppendFloatValue(value);
        return new MsgPackNode { Value = value, NodeType = MsgPackNodeType.Float, NumberFormat = MsgPackNumberFormat.Float64 };
    }
    
    private void AppendFloatValue(double value)
    {
        if (double.IsNaN(value))
            _jsonBuilder.Append("\"NaN\"");
        else if (double.IsPositiveInfinity(value))
            _jsonBuilder.Append("\"Infinity\"");
        else if (double.IsNegativeInfinity(value))
            _jsonBuilder.Append("\"-Infinity\"");
        else
            _jsonBuilder.Append(value.ToString("G", System.Globalization.CultureInfo.InvariantCulture));
    }

    private MsgPackNode ParseUInt8(string path)
    {
        _position++;
        byte value = _data[_position++];
        _jsonBuilder.Append(value);
        return new MsgPackNode { Value = (long)value, NodeType = MsgPackNodeType.Integer, NumberFormat = MsgPackNumberFormat.UInt8 };
    }

    private MsgPackNode ParseUInt16(string path)
    {
        _position++;
        ushort value = (ushort)((_data[_position] << 8) | _data[_position + 1]);
        _position += 2;
        _jsonBuilder.Append(value);
        return new MsgPackNode { Value = (long)value, NodeType = MsgPackNodeType.Integer, NumberFormat = MsgPackNumberFormat.UInt16 };
    }

    private MsgPackNode ParseUInt32(string path)
    {
        _position++;
        uint value = (uint)((_data[_position] << 24) | (_data[_position + 1] << 16) |
                           (_data[_position + 2] << 8) | _data[_position + 3]);
        _position += 4;
        _jsonBuilder.Append(value);
        return new MsgPackNode { Value = (long)value, NodeType = MsgPackNodeType.Integer, NumberFormat = MsgPackNumberFormat.UInt32 };
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
        return new MsgPackNode { Value = value, NodeType = MsgPackNodeType.Integer, NumberFormat = MsgPackNumberFormat.UInt64 };
    }

    private MsgPackNode ParseInt8(string path)
    {
        _position++;
        sbyte value = (sbyte)_data[_position++];
        _jsonBuilder.Append(value);
        return new MsgPackNode { Value = (long)value, NodeType = MsgPackNodeType.Integer, NumberFormat = MsgPackNumberFormat.Int8 };
    }

    private MsgPackNode ParseInt16(string path)
    {
        _position++;
        short value = (short)((_data[_position] << 8) | _data[_position + 1]);
        _position += 2;
        _jsonBuilder.Append(value);
        return new MsgPackNode { Value = (long)value, NodeType = MsgPackNodeType.Integer, NumberFormat = MsgPackNumberFormat.Int16 };
    }

    private MsgPackNode ParseInt32(string path)
    {
        _position++;
        int value = (_data[_position] << 24) | (_data[_position + 1] << 16) |
                    (_data[_position + 2] << 8) | _data[_position + 3];
        _position += 4;
        _jsonBuilder.Append(value);
        return new MsgPackNode { Value = (long)value, NodeType = MsgPackNodeType.Integer, NumberFormat = MsgPackNumberFormat.Int32 };
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
        return new MsgPackNode { Value = value, NodeType = MsgPackNodeType.Integer, NumberFormat = MsgPackNumberFormat.Int64 };
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
            node.Children.Add(keyNode);
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
            node.Children.Add(keyNode);
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
            node.Children.Add(keyNode);
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
            bool inString = false;
            
            while (compactIdx < json.Length && formattedIdx < formatted.Length)
            {
                char cc = json[compactIdx];
                
                // Skip whitespace in formatted JSON (only outside strings)
                if (!inString)
                {
                    while (formattedIdx < formatted.Length && char.IsWhiteSpace(formatted[formattedIdx]))
                    {
                        formattedIdx++;
                    }
                }
                
                if (formattedIdx >= formatted.Length) break;
                
                map[compactIdx] = formattedIdx;
                
                // Track string state for proper whitespace handling
                if (cc == '"' && (compactIdx == 0 || json[compactIdx - 1] != '\\'))
                {
                    inString = !inString;
                }
                
                compactIdx++;
                formattedIdx++;
            }
            
            // Fill remaining positions
            for (int i = compactIdx; i <= json.Length; i++)
            {
                map[i] = formatted.Length;
            }
            
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
        return MessagePackSerializer.ConvertFromJson(json);
    }

    public static byte[] RebuildFromJson(string editedJson, MsgPackNode? rootNode, byte[] originalData)
    {
        if (rootNode == null)
        {
            return MessagePackSerializer.ConvertFromJson(editedJson);
        }

        try
        {
            using var jsonDoc = JsonDocument.Parse(editedJson);
            using var ms = new MemoryStream();
            SerializeNode(ms, jsonDoc.RootElement, rootNode, originalData);
            return ms.ToArray();
        }
        catch
        {
            return MessagePackSerializer.ConvertFromJson(editedJson);
        }
    }

    private static void SerializeNode(MemoryStream ms, JsonElement jsonElement, MsgPackNode node, byte[] originalData)
    {
        switch (node.NodeType)
        {
            case MsgPackNodeType.Nil:
                ms.WriteByte(0xc0);
                break;
            case MsgPackNodeType.Boolean:
                ms.WriteByte(ReadBoolean(jsonElement, node.Value) ? (byte)0xc3 : (byte)0xc2);
                break;
            case MsgPackNodeType.Integer:
                if (IsUnsignedFormat(node.NumberFormat))
                {
                    ulong uValue = ReadUInt64(jsonElement, node.Value);
                    WriteUnsignedWithFormat(ms, uValue, node.NumberFormat);
                }
                else
                {
                    long iValue = ReadInt64(jsonElement, node.Value);
                    WriteIntegerWithFormat(ms, iValue, node.NumberFormat);
                }
                break;
            case MsgPackNodeType.Float:
                double floatValue = ReadDouble(jsonElement, node.Value);
                WriteFloatWithFormat(ms, floatValue, node.NumberFormat);
                break;
            case MsgPackNodeType.String:
                string strValue = jsonElement.ValueKind == JsonValueKind.String
                    ? jsonElement.GetString() ?? string.Empty
                    : jsonElement.ToString();
                WriteString(ms, strValue);
                break;
            case MsgPackNodeType.Binary:
                WriteBinary(ms, ReadBinary(jsonElement, node.Value));
                break;
            case MsgPackNodeType.Array:
                if (jsonElement.ValueKind != JsonValueKind.Array)
                    throw new InvalidOperationException("Array expected");
                if (jsonElement.GetArrayLength() != node.Children.Count)
                    throw new InvalidOperationException("Array length mismatch");
                WriteArrayHeader(ms, node.Children.Count);
                int arrayIndex = 0;
                foreach (var item in jsonElement.EnumerateArray())
                {
                    SerializeNode(ms, item, node.Children[arrayIndex], originalData);
                    arrayIndex++;
                }
                break;
            case MsgPackNodeType.Map:
                if (jsonElement.ValueKind != JsonValueKind.Object)
                    throw new InvalidOperationException("Object expected");
                var props = jsonElement.EnumerateObject().ToList();
                int expectedPairs = node.Children.Count / 2;
                if (props.Count != expectedPairs)
                    throw new InvalidOperationException("Map size mismatch");
                WriteMapHeader(ms, expectedPairs);
                for (int i = 0; i < expectedPairs; i++)
                {
                    var keyNode = node.Children[i * 2];
                    var valueNode = node.Children[i * 2 + 1];
                    var prop = props[i];
                    WriteMapKey(ms, keyNode, prop.Name, originalData);
                    SerializeNode(ms, prop.Value, valueNode, originalData);
                }
                break;
            case MsgPackNodeType.Extension:
                if (node.Value is ValueTuple<sbyte, byte[]> extValue)
                {
                    WriteExtension(ms, extValue.Item1, extValue.Item2);
                }
                else
                {
                    WriteOriginalBytes(ms, node, originalData);
                }
                break;
        }
    }

    private static void WriteMapKey(MemoryStream ms, MsgPackNode keyNode, string editedKey, byte[] originalData)
    {
        switch (keyNode.NodeType)
        {
            case MsgPackNodeType.String:
                WriteString(ms, editedKey);
                return;
            case MsgPackNodeType.Integer:
                if (IsUnsignedFormat(keyNode.NumberFormat))
                {
                    if (ulong.TryParse(editedKey, System.Globalization.NumberStyles.Integer,
                        System.Globalization.CultureInfo.InvariantCulture, out var uValue))
                    {
                        WriteUnsignedWithFormat(ms, uValue, keyNode.NumberFormat);
                    }
                    else if (keyNode.Value is ulong uOriginal)
                    {
                        WriteUnsignedWithFormat(ms, uOriginal, keyNode.NumberFormat);
                    }
                    else
                    {
                        WriteOriginalBytes(ms, keyNode, originalData);
                    }
                }
                else
                {
                    if (long.TryParse(editedKey, System.Globalization.NumberStyles.Integer,
                        System.Globalization.CultureInfo.InvariantCulture, out var iValue))
                    {
                        WriteIntegerWithFormat(ms, iValue, keyNode.NumberFormat);
                    }
                    else if (keyNode.Value is long iOriginal)
                    {
                        WriteIntegerWithFormat(ms, iOriginal, keyNode.NumberFormat);
                    }
                    else
                    {
                        WriteOriginalBytes(ms, keyNode, originalData);
                    }
                }
                return;
            case MsgPackNodeType.Boolean:
                ms.WriteByte(ReadBooleanFromString(editedKey, keyNode.Value) ? (byte)0xc3 : (byte)0xc2);
                return;
            case MsgPackNodeType.Nil:
                ms.WriteByte(0xc0);
                return;
            default:
                WriteOriginalBytes(ms, keyNode, originalData);
                return;
        }
    }

    private static bool ReadBoolean(JsonElement element, object? fallback)
    {
        return element.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.String => ReadBooleanFromString(element.GetString() ?? string.Empty, fallback),
            _ => fallback is bool b && b
        };
    }

    private static bool ReadBooleanFromString(string value, object? fallback)
    {
        if (bool.TryParse(value, out var result))
            return result;
        return fallback is bool b && b;
    }

    private static long ReadInt64(JsonElement element, object? fallback)
    {
        if (element.ValueKind == JsonValueKind.Number && element.TryGetInt64(out var value))
            return value;
        if (element.ValueKind == JsonValueKind.String && long.TryParse(element.GetString(),
            System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out value))
            return value;
        return fallback switch
        {
            long v => v,
            int v => v,
            short v => v,
            sbyte v => v,
            _ => 0
        };
    }

    private static ulong ReadUInt64(JsonElement element, object? fallback)
    {
        if (element.ValueKind == JsonValueKind.Number && element.TryGetUInt64(out var value))
            return value;
        if (element.ValueKind == JsonValueKind.String && ulong.TryParse(element.GetString(),
            System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out value))
            return value;
        return fallback switch
        {
            ulong v => v,
            uint v => v,
            ushort v => v,
            byte v => v,
            long v when v >= 0 => (ulong)v,
            _ => 0
        };
    }

    private static double ReadDouble(JsonElement element, object? fallback)
    {
        if (element.ValueKind == JsonValueKind.Number && element.TryGetDouble(out var value))
            return value;
        if (element.ValueKind == JsonValueKind.String)
            return ParseSpecialFloat(element.GetString() ?? string.Empty);
        return fallback switch
        {
            double v => v,
            float v => v,
            _ => 0
        };
    }

    private static byte[] ReadBinary(JsonElement element, object? fallback)
    {
        if (element.ValueKind == JsonValueKind.String)
        {
            try
            {
                return Convert.FromBase64String(element.GetString() ?? string.Empty);
            }
            catch
            {
                return Array.Empty<byte>();
            }
        }

        return fallback as byte[] ?? Array.Empty<byte>();
    }

    private static bool IsUnsignedFormat(MsgPackNumberFormat format)
    {
        return format is MsgPackNumberFormat.PositiveFixInt or MsgPackNumberFormat.UInt8 or
            MsgPackNumberFormat.UInt16 or MsgPackNumberFormat.UInt32 or MsgPackNumberFormat.UInt64;
    }

    private static void WriteIntegerWithFormat(MemoryStream ms, long value, MsgPackNumberFormat format)
    {
        switch (format)
        {
            case MsgPackNumberFormat.PositiveFixInt when value >= 0 && value <= 0x7f:
                ms.WriteByte((byte)value);
                return;
            case MsgPackNumberFormat.NegativeFixInt when value >= -32 && value < 0:
                ms.WriteByte((byte)value);
                return;
            case MsgPackNumberFormat.Int8 when value >= sbyte.MinValue && value <= sbyte.MaxValue:
                ms.WriteByte(0xd0);
                ms.WriteByte((byte)(sbyte)value);
                return;
            case MsgPackNumberFormat.Int16 when value >= short.MinValue && value <= short.MaxValue:
                ms.WriteByte(0xd1);
                WriteInt16(ms, (short)value);
                return;
            case MsgPackNumberFormat.Int32 when value >= int.MinValue && value <= int.MaxValue:
                ms.WriteByte(0xd2);
                WriteInt32(ms, (int)value);
                return;
            case MsgPackNumberFormat.Int64:
                ms.WriteByte(0xd3);
                WriteInt64(ms, value);
                return;
        }

        WriteInteger(ms, value);
    }

    private static void WriteUnsignedWithFormat(MemoryStream ms, ulong value, MsgPackNumberFormat format)
    {
        switch (format)
        {
            case MsgPackNumberFormat.PositiveFixInt when value <= 0x7f:
                ms.WriteByte((byte)value);
                return;
            case MsgPackNumberFormat.UInt8 when value <= byte.MaxValue:
                ms.WriteByte(0xcc);
                ms.WriteByte((byte)value);
                return;
            case MsgPackNumberFormat.UInt16 when value <= ushort.MaxValue:
                ms.WriteByte(0xcd);
                WriteUInt16(ms, (ushort)value);
                return;
            case MsgPackNumberFormat.UInt32 when value <= uint.MaxValue:
                ms.WriteByte(0xce);
                WriteUInt32(ms, (uint)value);
                return;
            case MsgPackNumberFormat.UInt64:
                ms.WriteByte(0xcf);
                WriteUInt64(ms, value);
                return;
        }

        if (value <= long.MaxValue)
        {
            WriteInteger(ms, (long)value);
        }
        else
        {
            ms.WriteByte(0xcf);
            WriteUInt64(ms, value);
        }
    }

    private static void WriteFloatWithFormat(MemoryStream ms, double value, MsgPackNumberFormat format)
    {
        if (format == MsgPackNumberFormat.Float32)
        {
            WriteFloat32(ms, (float)value);
        }
        else
        {
            WriteFloat64(ms, value);
        }
    }

    private static void WriteInteger(MemoryStream ms, long value)
    {
        if (value >= 0 && value <= 127)
        {
            ms.WriteByte((byte)value);
        }
        else if (value >= -32 && value < 0)
        {
            ms.WriteByte((byte)value);
        }
        else if (value >= 0 && value <= byte.MaxValue)
        {
            ms.WriteByte(0xcc);
            ms.WriteByte((byte)value);
        }
        else if (value >= 0 && value <= ushort.MaxValue)
        {
            ms.WriteByte(0xcd);
            WriteUInt16(ms, (ushort)value);
        }
        else if (value >= 0 && value <= uint.MaxValue)
        {
            ms.WriteByte(0xce);
            WriteUInt32(ms, (uint)value);
        }
        else if (value >= sbyte.MinValue && value <= sbyte.MaxValue)
        {
            ms.WriteByte(0xd0);
            ms.WriteByte((byte)(sbyte)value);
        }
        else if (value >= short.MinValue && value <= short.MaxValue)
        {
            ms.WriteByte(0xd1);
            WriteInt16(ms, (short)value);
        }
        else if (value >= int.MinValue && value <= int.MaxValue)
        {
            ms.WriteByte(0xd2);
            WriteInt32(ms, (int)value);
        }
        else
        {
            ms.WriteByte(0xd3);
            WriteInt64(ms, value);
        }
    }

    private static void WriteFloat32(MemoryStream ms, float value)
    {
        ms.WriteByte(0xca);
        byte[] bytes = BitConverter.GetBytes(value);
        if (BitConverter.IsLittleEndian)
            Array.Reverse(bytes);
        ms.Write(bytes, 0, bytes.Length);
    }

    private static void WriteFloat64(MemoryStream ms, double value)
    {
        ms.WriteByte(0xcb);
        byte[] bytes = BitConverter.GetBytes(value);
        if (BitConverter.IsLittleEndian)
            Array.Reverse(bytes);
        ms.Write(bytes, 0, bytes.Length);
    }

    private static void WriteString(MemoryStream ms, string value)
    {
        byte[] utf8 = Encoding.UTF8.GetBytes(value);
        int len = utf8.Length;
        if (len <= 31)
        {
            ms.WriteByte((byte)(0xa0 | len));
        }
        else if (len <= byte.MaxValue)
        {
            ms.WriteByte(0xd9);
            ms.WriteByte((byte)len);
        }
        else if (len <= ushort.MaxValue)
        {
            ms.WriteByte(0xda);
            WriteUInt16(ms, (ushort)len);
        }
        else
        {
            ms.WriteByte(0xdb);
            WriteUInt32(ms, (uint)len);
        }
        ms.Write(utf8, 0, utf8.Length);
    }

    private static void WriteBinary(MemoryStream ms, byte[] data)
    {
        int len = data.Length;
        if (len <= byte.MaxValue)
        {
            ms.WriteByte(0xc4);
            ms.WriteByte((byte)len);
        }
        else if (len <= ushort.MaxValue)
        {
            ms.WriteByte(0xc5);
            WriteUInt16(ms, (ushort)len);
        }
        else
        {
            ms.WriteByte(0xc6);
            WriteUInt32(ms, (uint)len);
        }
        ms.Write(data, 0, data.Length);
    }

    private static void WriteArrayHeader(MemoryStream ms, int count)
    {
        if (count <= 15)
        {
            ms.WriteByte((byte)(0x90 | count));
        }
        else if (count <= ushort.MaxValue)
        {
            ms.WriteByte(0xdc);
            WriteUInt16(ms, (ushort)count);
        }
        else
        {
            ms.WriteByte(0xdd);
            WriteUInt32(ms, (uint)count);
        }
    }

    private static void WriteMapHeader(MemoryStream ms, int count)
    {
        if (count <= 15)
        {
            ms.WriteByte((byte)(0x80 | count));
        }
        else if (count <= ushort.MaxValue)
        {
            ms.WriteByte(0xde);
            WriteUInt16(ms, (ushort)count);
        }
        else
        {
            ms.WriteByte(0xdf);
            WriteUInt32(ms, (uint)count);
        }
    }

    private static void WriteExtension(MemoryStream ms, sbyte type, byte[] data)
    {
        int len = data.Length;
        switch (len)
        {
            case 1:
                ms.WriteByte(0xd4);
                ms.WriteByte((byte)type);
                break;
            case 2:
                ms.WriteByte(0xd5);
                ms.WriteByte((byte)type);
                break;
            case 4:
                ms.WriteByte(0xd6);
                ms.WriteByte((byte)type);
                break;
            case 8:
                ms.WriteByte(0xd7);
                ms.WriteByte((byte)type);
                break;
            case 16:
                ms.WriteByte(0xd8);
                ms.WriteByte((byte)type);
                break;
            default:
                if (len <= byte.MaxValue)
                {
                    ms.WriteByte(0xc7);
                    ms.WriteByte((byte)len);
                    ms.WriteByte((byte)type);
                }
                else if (len <= ushort.MaxValue)
                {
                    ms.WriteByte(0xc8);
                    WriteUInt16(ms, (ushort)len);
                    ms.WriteByte((byte)type);
                }
                else
                {
                    ms.WriteByte(0xc9);
                    WriteUInt32(ms, (uint)len);
                    ms.WriteByte((byte)type);
                }
                break;
        }
        ms.Write(data, 0, data.Length);
    }

    private static void WriteUInt16(MemoryStream ms, ushort value)
    {
        ms.WriteByte((byte)(value >> 8));
        ms.WriteByte((byte)value);
    }

    private static void WriteUInt32(MemoryStream ms, uint value)
    {
        ms.WriteByte((byte)(value >> 24));
        ms.WriteByte((byte)(value >> 16));
        ms.WriteByte((byte)(value >> 8));
        ms.WriteByte((byte)value);
    }

    private static void WriteUInt64(MemoryStream ms, ulong value)
    {
        ms.WriteByte((byte)(value >> 56));
        ms.WriteByte((byte)(value >> 48));
        ms.WriteByte((byte)(value >> 40));
        ms.WriteByte((byte)(value >> 32));
        ms.WriteByte((byte)(value >> 24));
        ms.WriteByte((byte)(value >> 16));
        ms.WriteByte((byte)(value >> 8));
        ms.WriteByte((byte)value);
    }

    private static void WriteInt16(MemoryStream ms, short value)
    {
        WriteUInt16(ms, (ushort)value);
    }

    private static void WriteInt32(MemoryStream ms, int value)
    {
        WriteUInt32(ms, (uint)value);
    }

    private static void WriteInt64(MemoryStream ms, long value)
    {
        ms.WriteByte((byte)(value >> 56));
        ms.WriteByte((byte)(value >> 48));
        ms.WriteByte((byte)(value >> 40));
        ms.WriteByte((byte)(value >> 32));
        ms.WriteByte((byte)(value >> 24));
        ms.WriteByte((byte)(value >> 16));
        ms.WriteByte((byte)(value >> 8));
        ms.WriteByte((byte)value);
    }

    private static void WriteOriginalBytes(MemoryStream ms, MsgPackNode node, byte[] originalData)
    {
        if (node.StartOffset >= 0 && node.EndOffset > node.StartOffset && node.EndOffset <= originalData.Length)
        {
            ms.Write(originalData, node.StartOffset, node.EndOffset - node.StartOffset);
        }
        else
        {
            ms.WriteByte(0xc0);
        }
    }

    private static double ParseSpecialFloat(string str)
    {
        return str switch
        {
            "NaN" => double.NaN,
            "Infinity" => double.PositiveInfinity,
            "-Infinity" => double.NegativeInfinity,
            _ => double.TryParse(str, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var value)
                ? value
                : 0
        };
    }
}
