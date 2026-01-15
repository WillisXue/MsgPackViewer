using System.Collections.Generic;

namespace MsgPackViewer.Models;

public class MsgPackNode
{
    public int StartOffset { get; set; }
    public int EndOffset { get; set; }
    public string JsonPath { get; set; } = string.Empty;
    public object? Value { get; set; }
    public MsgPackNodeType NodeType { get; set; }
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
