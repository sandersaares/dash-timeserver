﻿using Koek;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using System;

namespace DashTimeserver.Server.Controllers
{
    /// <summary>
    /// Returns a timestamp to be used for clock synchronization between the ML-CDN Origin node and the player.
    /// </summary>
    [Route("")]
    [ApiController]
    public sealed class TimeController : ControllerBase
    {
        public TimeController(ITimeSource timeSource)
        {
            _timeSource = timeSource;
        }

        private readonly ITimeSource _timeSource;

        [HttpGet("")]
        public void Get()
        {
            Response.StatusCode = 400;
            Response.ContentType = "text/plain";

            Response.WriteAsync("Incomplete timestamp request URL. See dash-timeserver readme for instructions.");
        }

        /// <summary>
        /// Gets the current time as a string.
        /// </summary>
        /// <remarks>
        /// An offset can be applied for testing with "wrong" but still synchronized time. Nothing in the pipeline can rely on
        /// the clocks being *correct* - the most we can assume is that clocks are in sync between the ML-CDN Origin and the player.
        /// </remarks>
        [HttpGet("xsdatetime")]
        public string CurrentTimeAsXsDateTime([FromQuery] double? offsetSeconds)
        {
            return GetTime(offsetSeconds).ToString(Constants.XsDatetimeCompatibleFormatString);
        }

        /// <summary>
        /// Gets the current time as a .NET DateTimeOffset tick count in the UTZ timezone.
        /// </summary>
        /// <remarks>
        /// An offset can be applied for testing with "wrong" but still synchronized time. Nothing in the pipeline can rely on
        /// the clocks being *correct* - the most we can assume is that clocks are in sync between the ML-CDN Origin and the player.
        /// </remarks>
        [HttpGet("utcticks")]
        public long CurrentTimeAsUtcTicks([FromQuery] double? offsetSeconds)
        {
            return GetTime(offsetSeconds).Ticks;
        }

        private DateTimeOffset GetTime(double? offsetSeconds)
        {
            var now = _timeSource.GetCurrentTime();

            if (offsetSeconds.HasValue)
                now = now.AddSeconds(offsetSeconds.Value);

            return now;
        }
    }
}
