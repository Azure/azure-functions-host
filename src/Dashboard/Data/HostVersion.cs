namespace Dashboard.Data
{
    /// <summary>Represents a host version compatibility warning.</summary>
    public class HostVersion
    {
        /// <summary>Gets or sets a label describing the required feature.</summary>
        public string Label { get; set; }

        /// <summary>Gets or sets a link with more compatibility information.</summary>
        public string Link { get; set; }
    }
}
