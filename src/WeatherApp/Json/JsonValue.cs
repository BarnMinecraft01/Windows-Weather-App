using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace WeatherApp.Json
{
    public enum JsonKind
    {
        Null,
        Boolean,
        Number,
        String,
        Array,
        Object
    }

    /// <summary>
    /// A forgiving JSON value.
    ///
    /// Every accessor on this type is total: indexing a missing key, indexing an
    /// array out of range, or asking a string for its numeric value all return a
    /// default instead of throwing. That matters here because the app talks to
    /// several government endpoints whose payloads carry optional fields, and a
    /// missing "probabilityOfThunder" should degrade one label rather than take
    /// down the whole refresh.
    /// </summary>
    public sealed class JsonValue
    {
        private static readonly JsonValue NullInstance = new JsonValue();

        private readonly JsonKind _kind;
        private readonly bool _boolean;
        private readonly double _number;
        private readonly string _string;
        private readonly List<JsonValue> _array;
        private readonly Dictionary<string, JsonValue> _object;

        private JsonValue()
        {
            _kind = JsonKind.Null;
        }

        internal JsonValue(bool value)
        {
            _kind = JsonKind.Boolean;
            _boolean = value;
        }

        internal JsonValue(double value)
        {
            _kind = JsonKind.Number;
            _number = value;
        }

        internal JsonValue(string value)
        {
            _kind = JsonKind.String;
            _string = value;
        }

        internal JsonValue(List<JsonValue> value)
        {
            _kind = JsonKind.Array;
            _array = value;
        }

        internal JsonValue(Dictionary<string, JsonValue> value)
        {
            _kind = JsonKind.Object;
            _object = value;
        }

        public static JsonValue Null { get { return NullInstance; } }

        public JsonKind Kind { get { return _kind; } }

        public bool IsNull { get { return _kind == JsonKind.Null; } }

        /// <summary>True when this value actually carries data worth reading.</summary>
        public bool Exists { get { return _kind != JsonKind.Null; } }

        public bool IsArray { get { return _kind == JsonKind.Array; } }

        public bool IsObject { get { return _kind == JsonKind.Object; } }

        public static JsonValue Parse(string text)
        {
            return JsonParser.Parse(text);
        }

        public static bool TryParse(string text, out JsonValue value)
        {
            try
            {
                value = JsonParser.Parse(text);
                return true;
            }
            catch (Exception)
            {
                value = NullInstance;
                return false;
            }
        }

        public JsonValue this[string key]
        {
            get
            {
                if (_kind != JsonKind.Object || key == null) return NullInstance;
                JsonValue found;
                return _object.TryGetValue(key, out found) ? found : NullInstance;
            }
        }

        public JsonValue this[int index]
        {
            get
            {
                if (_kind != JsonKind.Array) return NullInstance;
                if (index < 0 || index >= _array.Count) return NullInstance;
                return _array[index];
            }
        }

        public int Count
        {
            get
            {
                if (_kind == JsonKind.Array) return _array.Count;
                if (_kind == JsonKind.Object) return _object.Count;
                return 0;
            }
        }

        /// <summary>Array elements, or an empty sequence for any other kind.</summary>
        public IEnumerable<JsonValue> Items
        {
            get
            {
                if (_kind != JsonKind.Array) return Enumerable.Empty<JsonValue>();
                return _array;
            }
        }

        public IEnumerable<string> Keys
        {
            get
            {
                if (_kind != JsonKind.Object) return Enumerable.Empty<string>();
                return _object.Keys;
            }
        }

        public bool ContainsKey(string key)
        {
            return _kind == JsonKind.Object && key != null && _object.ContainsKey(key);
        }

        /// <summary>
        /// Walks a chain of object keys, e.g. Path("properties", "elevation", "value").
        /// </summary>
        public JsonValue Path(params string[] keys)
        {
            JsonValue current = this;
            for (int i = 0; i < keys.Length; i++)
            {
                current = current[keys[i]];
                if (current.IsNull) return NullInstance;
            }
            return current;
        }

        public string AsString(string fallback = null)
        {
            switch (_kind)
            {
                case JsonKind.String:
                    return _string;
                case JsonKind.Number:
                    return _number.ToString(CultureInfo.InvariantCulture);
                case JsonKind.Boolean:
                    return _boolean ? "true" : "false";
                default:
                    return fallback;
            }
        }

        public double AsDouble(double fallback = 0d)
        {
            if (_kind == JsonKind.Number) return _number;
            if (_kind == JsonKind.Boolean) return _boolean ? 1d : 0d;
            if (_kind == JsonKind.String)
            {
                double parsed;
                if (double.TryParse(_string, NumberStyles.Float, CultureInfo.InvariantCulture, out parsed))
                {
                    return parsed;
                }
            }
            return fallback;
        }

        /// <summary>Returns null when the value is absent, so callers can show "--".</summary>
        public double? AsNullableDouble()
        {
            if (_kind == JsonKind.Number) return _number;
            if (_kind == JsonKind.String)
            {
                double parsed;
                if (double.TryParse(_string, NumberStyles.Float, CultureInfo.InvariantCulture, out parsed))
                {
                    return parsed;
                }
            }
            return null;
        }

        public int AsInt(int fallback = 0)
        {
            double value = AsDouble(fallback);
            if (double.IsNaN(value) || double.IsInfinity(value)) return fallback;
            if (value > int.MaxValue) return int.MaxValue;
            if (value < int.MinValue) return int.MinValue;
            return (int)Math.Round(value, MidpointRounding.AwayFromZero);
        }

        public bool AsBool(bool fallback = false)
        {
            if (_kind == JsonKind.Boolean) return _boolean;
            if (_kind == JsonKind.Number) return Math.Abs(_number) > double.Epsilon;
            if (_kind == JsonKind.String)
            {
                bool parsed;
                if (bool.TryParse(_string, out parsed)) return parsed;
            }
            return fallback;
        }

        /// <summary>
        /// Parses an ISO-8601 timestamp and returns the wall-clock time as issued.
        ///
        /// NWS stamps carry the forecast office's own offset ("2026-08-25T14:00:00-07:00"),
        /// and Open-Meteo, queried with timezone=auto, returns bare local times. Taking
        /// the local component of both means every time in the app is the local time at
        /// the place being forecast -- which is what a weather product should show, and
        /// what keeps the two sources comparable when the user looks at another region.
        /// </summary>
        public DateTime? AsDateTime()
        {
            if (_kind != JsonKind.String || string.IsNullOrEmpty(_string)) return null;

            // Some NWS fields are intervals ("2026-08-25T14:00:00-07:00/PT1H");
            // the instant before the slash is the part we want.
            string text = _string;
            int slash = text.IndexOf('/');
            if (slash > 0) text = text.Substring(0, slash);

            DateTimeOffset offset;
            if (DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture,
                    DateTimeStyles.None, out offset))
            {
                return offset.DateTime;
            }

            DateTime plain;
            if (DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.None, out plain))
            {
                return plain;
            }
            return null;
        }

        public override string ToString()
        {
            return AsString(string.Empty) ?? string.Empty;
        }
    }
}
