namespace KCTools.Models
{
    /// <summary>AT/diag sayfasındaki COM port seçeneği.</summary>
    public sealed class ComPortOption
    {
        public string Name { get; set; } = string.Empty;

        public string Detail { get; set; } = string.Empty;

        public string Display =>
            string.IsNullOrWhiteSpace(Detail) ? Name : Name + " · " + Detail;
    }
}
