namespace WebAPIDateTrendSelector.Models
{
    // ── Auth ──
    public class TokenResponse
    {
        public string? access_token { get; set; }
        public int     expires_in   { get; set; }
    }

    // ── Trend Collector ──
    public class TrendCollector
    {
        public string? ObjectId                    { get; set; }
        public string? PropertyIndex               { get; set; }
        public string? PropertyName                { get; set; }
        public string? CollectorObjectOrPropertyId { get; set; }
        public string? TrendseriesId               { get; set; }
        public string? TrendType                   { get; set; }
    }

    // ── Trend Series ──
    public class TrendSeries
    {
        public string?          Id               { get; set; }
        public string?          SeriesPropertyId { get; set; }
        public List<TrendPoint> Series           { get; set; } = new();
    }

    public class TrendPoint
    {
        public string?  Value        { get; set; }
        public string?  DisplayValue { get; set; }
        public string?  Quality      { get; set; }
        public bool     QualityGood  { get; set; }
        public DateTime Timestamp    { get; set; }
    }

    // ── Systems ──
    public class SystemsRepresentation
    {
        public List<LocalSystem> Systems   { get; set; } = new();
        public List<Language>    Languages { get; set; } = new();
    }

    public class LocalSystem
    {
        public string? ProjectName { get; set; }
        public string? Name        { get; set; }
        public int?    Id          { get; set; }
        public bool    IsOnline    { get; set; }
    }

    public class Language
    {
        public int?    ArrayIndex { get; set; }
        public string? Descriptor { get; set; }
        public string? Code       { get; set; }
    }

    // ── System Browser ──
    public class SystemBrowserResponse
    {
        public int        Total { get; set; }
        public int        Page  { get; set; }
        public int        Size  { get; set; }
        public List<Node> Nodes { get; set; } = new();
        public List<Link> Links { get; set; } = new();
    }

    public class Node
    {
        public bool          HasChild    { get; set; }
        public int           ViewId      { get; set; }
        public int           ViewType    { get; set; }
        public NodeAttribute Attributes  { get; set; } = new();
        public string        Location    { get; set; } = "";
        public int           SystemId    { get; set; }
        public string        Name        { get; set; } = "";
        public string        Descriptor  { get; set; } = "";
        public string        Designation { get; set; } = "";
        public string        ObjectId    { get; set; } = "";
        public object?       Links       { get; set; }
    }

    public class NodeAttribute
    {
        public string DefaultProperty         { get; set; } = "";
        public string ObjectId                { get; set; } = "";
        public string DisciplineDescriptor    { get; set; } = "";
        public int    DisciplineId            { get; set; }
        public string SubDisciplineDescriptor { get; set; } = "";
        public int    SubDisciplineId         { get; set; }
        public string TypeDescriptor          { get; set; } = "";
        public int    TypeId                  { get; set; }
        public string SubTypeDescriptor       { get; set; } = "";
        public int    SubTypeId               { get; set; }
        public int    ManagedType             { get; set; }
        public string ManagedTypeName         { get; set; } = "";
        public string ObjectModelName         { get; set; } = "";
    }

    public class Link
    {
        public string Rel         { get; set; } = "";
        public string Href        { get; set; } = "";
        public bool   IsTemplated { get; set; }
    }

    // ── 🆕 Trend Item DTO (pour la liste de sélection) ──
    public class TrendItemDto
    {
        public string TrendseriesId     { get; set; } = "";
        public string ObjectId          { get; set; } = "";
        public string FormattedLocation { get; set; } = "";
    }

    // ── Request / Response DTO ──
    public class TrendRequestDto
    {
        /// <summary>
        /// "single" = une journée | "range" = période
        /// </summary>
        public string  Mode       { get; set; } = "single";
        public string? DateSingle { get; set; }  // yyyy-MM-dd
        public string? DateFrom   { get; set; }  // yyyy-MM-dd
        public string? DateTo     { get; set; }  // yyyy-MM-dd

        /// <summary>
        /// 🆕 Liste des TrendseriesId sélectionnés — null ou vide = tous
        /// </summary>
        public List<string>? SelectedIds { get; set; }
    }

    public class TrendResultDto
    {
        public bool   Success     { get; set; }
        public string Message     { get; set; } = "";
        public int    TotalSeries { get; set; }
        public int    TotalValues { get; set; }
        public double DurationSec { get; set; }
    }

    // ── SignalR Progress ──
    public class ProgressUpdate
    {
        public int    Current  { get; set; }
        public int    Total    { get; set; }
        public int    Percent  { get; set; }
        public string Location { get; set; } = "";
        public string Log      { get; set; } = "";
    }
}