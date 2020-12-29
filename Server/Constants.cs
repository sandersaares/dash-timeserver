namespace DashTimeserver.Server
{
    public static class Constants
    {
        // String must conform to the xs:dateTime schema from XML.
        // Time server must be referenced in DASH MPD as urn:mpeg:dash:utc:http-xsdate:2014.
        public const string XsDatetimeCompatibleFormatString = "yyyy-MM-ddTHH:mm:ss.fffZ";
    }
}
