using System.Collections.Generic;

namespace MsgPackViewer.Models;

public class MsgPackNode
{
    public int StartOffset { get; set; }
    public int EndOffset { get; set; }
    public string JsonPath { get; set; } = string.Empty;
    public object? Value { get; set; }
    public MsgPackNodeType NodeType { get; set; }
    public MsgPackNumberFormat NumberFormat { get; set; } = MsgPackNumberFormat.None;
    public List<MsgPackNode> Children { get; set; } = new();
    
    public int JsonStartIndex { get; set; }
    public int JsonEndIndex { get; set; }
}

public enum MsgPackNodeType
{
    Nil,
    Boolean,
    Integer,
    Float,
    String,
    Binary,
    Array,
    Map,
    Extension
}

public enum MsgPackNumberFormat
{
    None,
    PositiveFixInt,
    NegativeFixInt,
    UInt8,
    UInt16,
    UInt32,
    UInt64,
    Int8,
    Int16,
    Int32,
    Int64,
    Float32,
    Float64
}
