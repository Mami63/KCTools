using System;
using System.Collections.Generic;
using System.Text;
using KCTools.Common;

namespace KCTools.Protocol
{
    /// <summary>Firehose cihaz yanıtı.</summary>
    public sealed class FirehoseResponse
    {
        public string Value { get; set; } = string.Empty;

        public bool RawMode { get; set; }

        public string Error { get; set; }

        public bool IsAck => string.Equals(Value, "ACK", StringComparison.OrdinalIgnoreCase);

        public override string ToString()
        {
            var text = Value;

            if (RawMode)
            {
                text += " (raw)";
            }

            if (!string.IsNullOrWhiteSpace(Error))
            {
                text += " · " + Error;
            }

            return text;
        }
    }

    public abstract class FirehoseEvent
    {
    }

    public sealed class FirehoseLogEvent : FirehoseEvent
    {
        public FirehoseLogEvent(string value)
        {
            Value = value ?? string.Empty;
        }

        public string Value { get; }
    }

    public sealed class FirehoseDataEvent : FirehoseEvent
    {
        public FirehoseDataEvent(byte[] data)
        {
            Data = data ?? Array.Empty<byte>();
        }

        public byte[] Data { get; }
    }

    public sealed class FirehoseResponseEvent : FirehoseEvent
    {
        public FirehoseResponseEvent(FirehoseResponse response)
        {
            Response = response;
        }

        public FirehoseResponse Response { get; }
    }

    /// <summary>
    /// Firehose yanıt akışını parça parça ayrıştırır: &lt;log&gt;, &lt;data&gt; (hex) ve
    /// &lt;response&gt; etiketleri. Gelen veri sınırları etiket ortasına düşse bile
    /// tampon bir sonraki parçayı bekler.
    /// </summary>
    public sealed class FirehosePacketParser
    {
        private readonly object _gate = new object();
        private readonly StringBuilder _buffer = new StringBuilder();

        public IReadOnlyList<FirehoseEvent> Feed(string chunk)
        {
            lock (_gate)
            {
                if (!string.IsNullOrEmpty(chunk))
                {
                    _buffer.Append(chunk);
                }

                return ParseLocked();
            }
        }

        public void Reset()
        {
            lock (_gate)
            {
                _buffer.Clear();
            }
        }

        public string Pending
        {
            get
            {
                lock (_gate)
                {
                    return _buffer.ToString();
                }
            }
        }

        private List<FirehoseEvent> ParseLocked()
        {
            var events = new List<FirehoseEvent>();

            while (true)
            {
                var text = _buffer.ToString();
                var start = text.IndexOf('<');

                if (start < 0)
                {
                    _buffer.Clear();
                    break;
                }

                if (start > 0)
                {
                    _buffer.Remove(0, start);
                    text = _buffer.ToString();
                }

                if (text.StartsWith("<data>", StringComparison.OrdinalIgnoreCase))
                {
                    var end = text.IndexOf("</data>", StringComparison.OrdinalIgnoreCase);

                    if (end < 0)
                    {
                        break;
                    }

                    var hex = text.Substring(6, end - 6);
                    _buffer.Remove(0, end + 7);

                    var bytes = Hex.Parse(hex);

                    if (bytes.Length > 0)
                    {
                        events.Add(new FirehoseDataEvent(bytes));
                    }

                    continue;
                }

                var close = text.IndexOf("/>", StringComparison.Ordinal);

                if (close < 0)
                {
                    break;
                }

                var tag = text.Substring(0, close + 2);
                _buffer.Remove(0, close + 2);

                if (tag.StartsWith("<log", StringComparison.OrdinalIgnoreCase))
                {
                    events.Add(new FirehoseLogEvent(Attribute(tag, "value")));
                }
                else if (tag.StartsWith("<response", StringComparison.OrdinalIgnoreCase))
                {
                    events.Add(new FirehoseResponseEvent(new FirehoseResponse
                    {
                        Value = Attribute(tag, "value") ?? "NAK",
                        RawMode = string.Equals(
                            Attribute(tag, "rawmode"), "true", StringComparison.OrdinalIgnoreCase),
                        Error = Attribute(tag, "error")
                    }));
                }
            }

            return events;
        }

        private static string Attribute(string tag, string name)
        {
            var pattern = name + "=\"";
            var index = tag.IndexOf(pattern, StringComparison.OrdinalIgnoreCase);

            if (index < 0)
            {
                return null;
            }

            var start = index + pattern.Length;
            var end = tag.IndexOf('"', start);

            return end < 0 ? null : Unescape(tag.Substring(start, end - start));
        }

        private static string Unescape(string value)
        {
            return value
                .Replace("&quot;", "\"")
                .Replace("&apos;", "'")
                .Replace("&lt;", "<")
                .Replace("&gt;", ">")
                .Replace("&amp;", "&");
        }
    }
}
