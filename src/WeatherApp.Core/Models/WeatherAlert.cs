using System;
using System.Collections.Generic;

namespace WeatherApp.Models
{
    /// <summary>An active NWS watch, warning, advisory or statement.</summary>
    public sealed class WeatherAlert
    {
        public string Id { get; set; }
        public string Event { get; set; }
        public string Headline { get; set; }
        public string Description { get; set; }
        public string Instruction { get; set; }
        public string AreaDescription { get; set; }
        public string Severity { get; set; }
        public string Certainty { get; set; }
        public string Urgency { get; set; }
        public string SenderName { get; set; }
        public DateTime? Onset { get; set; }
        public DateTime? Expires { get; set; }
        public DateTime? Effective { get; set; }

        /// <summary>
        /// Sort weight so the list leads with what matters. NWS severity is a coarse
        /// scale, so the event name is used to lift the handful of products that are
        /// genuinely life-threatening above same-severity siblings.
        /// </summary>
        public int Priority
        {
            get
            {
                string name = Event ?? string.Empty;

                if (name.IndexOf("Tornado Warning", StringComparison.OrdinalIgnoreCase) >= 0) return 0;
                if (name.IndexOf("Flash Flood Emergency", StringComparison.OrdinalIgnoreCase) >= 0) return 0;
                if (name.IndexOf("Tsunami Warning", StringComparison.OrdinalIgnoreCase) >= 0) return 0;

                string severity = (Severity ?? string.Empty).ToLowerInvariant();
                switch (severity)
                {
                    case "extreme": return 1;
                    case "severe": return 2;
                    case "moderate": return 3;
                    case "minor": return 4;
                    default: return 5;
                }
            }
        }

        public bool IsWarning
        {
            get
            {
                return (Event ?? string.Empty).IndexOf("Warning", StringComparison.OrdinalIgnoreCase) >= 0;
            }
        }

        public bool IsWatch
        {
            get
            {
                return (Event ?? string.Empty).IndexOf("Watch", StringComparison.OrdinalIgnoreCase) >= 0;
            }
        }
    }

    /// <summary>Alerts plus what they were fetched for, so the UI can label the list.</summary>
    public sealed class AlertCollection
    {
        public AlertCollection()
        {
            Alerts = new List<WeatherAlert>();
        }

        public List<WeatherAlert> Alerts { get; private set; }
        public string ScopeDescription { get; set; }
        public DateTime RetrievedAt { get; set; }
    }
}
