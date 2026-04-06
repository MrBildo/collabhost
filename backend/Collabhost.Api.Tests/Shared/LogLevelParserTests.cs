using Collabhost.Api.Shared;

using Shouldly;

using Xunit;

namespace Collabhost.Api.Tests.Shared;

public class LogLevelParserTests
{
    // --- .NET Microsoft.Extensions.Logging ---

    [Fact]
    public void ParseLevel_MicrosoftInfo_ReturnsINF() =>
        LogLevelParser.ParseLevel("info: Microsoft.Hosting.Lifetime[0]").ShouldBe("INF");

    [Fact]
    public void ParseLevel_MicrosoftWarn_ReturnsWRN() =>
        LogLevelParser.ParseLevel("warn: Some warning message").ShouldBe("WRN");

    [Fact]
    public void ParseLevel_MicrosoftFail_ReturnsERR() =>
        LogLevelParser.ParseLevel("fail: Unhandled exception").ShouldBe("ERR");

    [Fact]
    public void ParseLevel_MicrosoftCrit_ReturnsFTL() =>
        LogLevelParser.ParseLevel("crit: Fatal system failure").ShouldBe("FTL");

    [Fact]
    public void ParseLevel_MicrosoftDbug_ReturnsDBG() =>
        LogLevelParser.ParseLevel("dbug: Connection pool stats").ShouldBe("DBG");

    [Fact]
    public void ParseLevel_MicrosoftTrce_ReturnsDBG() =>
        LogLevelParser.ParseLevel("trce: Detailed trace message").ShouldBe("DBG");

    [Fact]
    public void ParseLevel_MicrosoftCaseInsensitive_ReturnsINF() =>
        LogLevelParser.ParseLevel("INFO: Application started").ShouldBe("INF");

    // --- Serilog bracket format ---

    [Fact]
    public void ParseLevel_SerilogINF_ReturnsINF() =>
        LogLevelParser.ParseLevel("[10:30:00 INF] Starting application").ShouldBe("INF");

    [Fact]
    public void ParseLevel_SerilogWRN_ReturnsWRN() =>
        LogLevelParser.ParseLevel("[WRN] Slow query detected").ShouldBe("WRN");

    [Fact]
    public void ParseLevel_SerilogERR_ReturnsERR() =>
        LogLevelParser.ParseLevel("[ERR] NullReferenceException in Handler").ShouldBe("ERR");

    [Fact]
    public void ParseLevel_SerilogDBG_ReturnsDBG() =>
        LogLevelParser.ParseLevel("[DBG] Resolving dependency").ShouldBe("DBG");

    [Fact]
    public void ParseLevel_SerilogFTL_ReturnsFTL() =>
        LogLevelParser.ParseLevel("[FTL] Unrecoverable error").ShouldBe("FTL");

    [Fact]
    public void ParseLevel_SerilogVRB_ReturnsDBG() =>
        LogLevelParser.ParseLevel("[VRB] Verbose trace").ShouldBe("DBG");

    // --- Generic / Node / Python ---

    [Fact]
    public void ParseLevel_GenericINFO_ReturnsINF() =>
        LogLevelParser.ParseLevel("INFO Starting server on port 3000").ShouldBe("INF");

    [Fact]
    public void ParseLevel_GenericERROR_ReturnsERR() =>
        LogLevelParser.ParseLevel("ERROR Failed to connect to database").ShouldBe("ERR");

    [Fact]
    public void ParseLevel_PythonWARNING_ReturnsWRN() =>
        LogLevelParser.ParseLevel("WARNING: Deprecated API usage").ShouldBe("WRN");

    [Fact]
    public void ParseLevel_PythonCRITICAL_ReturnsFTL() =>
        LogLevelParser.ParseLevel("CRITICAL: System out of memory").ShouldBe("FTL");

    [Fact]
    public void ParseLevel_GenericAfterTimestamp_ReturnsINF() =>
        LogLevelParser.ParseLevel("2024-01-15 10:30:00 INFO Application started").ShouldBe("INF");

    [Fact]
    public void ParseLevel_GenericDEBUG_ReturnsDBG() =>
        LogLevelParser.ParseLevel("DEBUG Processing request id=42").ShouldBe("DBG");

    // --- Edge cases ---

    [Fact]
    public void ParseLevel_NullLine_ReturnsNull() =>
        LogLevelParser.ParseLevel(null).ShouldBeNull();

    [Fact]
    public void ParseLevel_EmptyLine_ReturnsNull() =>
        LogLevelParser.ParseLevel("").ShouldBeNull();

    [Fact]
    public void ParseLevel_WhitespaceLine_ReturnsNull() =>
        LogLevelParser.ParseLevel("   ").ShouldBeNull();

    [Fact]
    public void ParseLevel_NoLevelInfo_ReturnsNull() =>
        LogLevelParser.ParseLevel("Just a regular log line with no level").ShouldBeNull();

    [Fact]
    public void ParseLevel_GenericFATAL_ReturnsFTL() =>
        LogLevelParser.ParseLevel("FATAL Unrecoverable error in worker").ShouldBe("FTL");

    // --- Pattern priority ---

    [Fact]
    public void ParseLevel_MultiplePatternMatch_MicrosoftWinsBecauseCheckedFirst() =>
        // "info:" matches Microsoft, "[INF]" matches Serilog — Microsoft is checked first
        LogLevelParser.ParseLevel("info: [INF] Starting").ShouldBe("INF");

    // --- Embedded keyword false-positives ---

    [Fact]
    public void ParseLevel_EmbeddedKeywordINFORMATION_ReturnsNull() =>
        // "INFORMATION" contains "INFO" but \b boundary prevents matching mid-word
        LogLevelParser.ParseLevel("INFORMATION service started").ShouldBeNull();

    [Fact]
    public void ParseLevel_EmbeddedKeywordWARNING_LEVEL_ReturnsNull() =>
        // "WARNING_LEVEL" — underscore is a word character, so \b prevents matching
        LogLevelParser.ParseLevel("WARNING_LEVEL is set").ShouldBeNull();

    // --- ANSI escape sequences ---

    [Fact]
    public void ParseLevel_MicrosoftInfoWithAnsi_ReturnsINF() =>
        LogLevelParser.ParseLevel("\x1b[32minfo\x1b[0m: Application started").ShouldBe("INF");

    [Fact]
    public void ParseLevel_SerilogWithAnsi_ReturnsINF() =>
        LogLevelParser.ParseLevel("\x1b[36m[10:30:00 INF]\x1b[0m Starting").ShouldBe("INF");

    [Fact]
    public void ParseLevel_GenericErrorWithAnsi_ReturnsERR() =>
        LogLevelParser.ParseLevel("\x1b[31mERROR\x1b[0m: Something failed").ShouldBe("ERR");

    [Fact]
    public void ParseLevel_OnlyAnsi_ReturnsNull() =>
        LogLevelParser.ParseLevel("\x1b[32m\x1b[0m").ShouldBeNull();

    [Fact]
    public void ParseLevel_NoAnsi_StillWorks() =>
        // Regression test: clean input still works after ANSI stripping was added
        LogLevelParser.ParseLevel("info: Normal line").ShouldBe("INF");
}
