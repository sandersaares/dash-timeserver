# DASH timeserver

This repo contains:

* A time server capable of emitting a millisecond-precision MPEG-DASH clock synchronization signal.
* A .NET Standard 2.0 client library for synchronizing against such a time signal.

The latter is published as [DashTimeserver.Client on nuget.org](https://www.nuget.org/packages/dashtimeserver.client).

# HTTP endpoints

| URL           | Usage                                                                       |
|---------------|-----------------------------------------------------------------------------|
| `/xsdatetime` | Timestamp in `xs:datetime` format from XML Schema, with fractional seconds. |
| `/utcticks`   | Timestamp as .NET DateTime ticks in UTC timezone.                           |

Use `/xsdatetime` for MPEG-DASH compatible output.

# Query string parameters

| Name            | Usage                                                                                 |
|-----------------|---------------------------------------------------------------------------------------|
| `offsetSeconds` | Offset to add to the returned timestamp, in seconds. Floating point values supported. |